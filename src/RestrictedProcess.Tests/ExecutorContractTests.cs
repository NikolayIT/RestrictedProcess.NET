namespace RestrictedProcess.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    using Xunit;

    /// <summary>
    /// The contract of the executor itself: which limit produces which verdict, how the standard IO edges
    /// behave, and whether an executor survives being reused and used concurrently.
    /// </summary>
    public class ExecutorContractTests : BaseExecutorsTestClass
    {
        private const string SleepSourceCode = @"using System;
using System.Threading;
class Program
{
    public static void Main(string[] args)
    {
        Thread.Sleep(int.Parse(args[0]));
        Console.WriteLine(""SLEPT"");
    }
}";

        private const string BurnCpuSourceCode = @"using System;
using System.Diagnostics;
class Program
{
    public static void Main(string[] args)
    {
        // Burn a fixed amount of *processor* time, not wall time. Spinning for a wall-clock duration
        // accumulates however much CPU the machine happens to spare, which on a loaded build agent can be
        // a fraction of it - and then an assertion about processor time fails for no good reason.
        var self = Process.GetCurrentProcess();
        var target = TimeSpan.FromMilliseconds(int.Parse(args[0]));
        long counter = 0;
        while (self.TotalProcessorTime < target)
        {
            for (var i = 0; i < 200000; i++) { counter++; }
            self.Refresh();
        }

        Console.WriteLine(counter);
    }
}";

        private const string ExitWithCodeSourceCode = @"using System;
class Program
{
    public static void Main(string[] args)
    {
        Environment.Exit(int.Parse(args[0]));
    }
}";

        private const string LaunchChildSourceCode = @"using System;
using System.Diagnostics;
class Program
{
    public static void Main(string[] args)
    {
        var info = new ProcessStartInfo(args[0], ""700"");
        info.UseShellExecute = false;
        var child = Process.Start(info);
        child.WaitForExit();
        Console.WriteLine(""child-exited"");
    }
}";

        public static TheoryData<int> ExitCodes => new TheoryData<int> { 0, 1, 42, 259, -1 };

        [Fact]
        public void SleepingDoesNotConsumeTheProcessorTimeLimit()
        {
            // The whole reason the two limits are separate: a program that blocks burns no processor time,
            // so a generous wall clock must let it finish even under a tight processor time limit.
            var exePath = this.CreateExe("SleepUnderCpuLimit.exe", SleepSourceCode);

            var request = new ExecutionRequest(exePath)
            {
                Arguments = new[] { "3000" },
                CpuTimeLimit = TimeSpan.FromSeconds(2),
                WallClockLimit = TimeSpan.FromSeconds(30),
                MemoryLimitBytes = 64 * 1024 * 1024,
            };

            var result = new RestrictedProcessExecutor().Execute(request);

            Assert.Equal(ProcessExecutionResultType.Success, result.Type);
            Assert.Equal("SLEPT", result.ReceivedOutput.Trim());
            Assert.True(result.TimeWorked > TimeSpan.FromSeconds(2), $"Only ran for {result.TimeWorked}.");
            Assert.True(
                result.TotalProcessorTime < TimeSpan.FromSeconds(2),
                $"A sleeping program somehow used {result.TotalProcessorTime} of processor time.");
        }

        [Fact]
        public void TheWallClockDeadlineStopsASleepingProgram()
        {
            var exePath = this.CreateExe("SleepPastWallClock.exe", SleepSourceCode);

            var request = new ExecutionRequest(exePath)
            {
                Arguments = new[] { "30000" },
                CpuTimeLimit = TimeSpan.FromSeconds(60),
                WallClockLimit = TimeSpan.FromSeconds(1),
                MemoryLimitBytes = 64 * 1024 * 1024,
            };

            var stopwatch = Stopwatch.StartNew();
            var result = new RestrictedProcessExecutor().Execute(request);
            stopwatch.Stop();

            Assert.Equal(ProcessExecutionResultType.TimeLimit, result.Type);
            Assert.DoesNotContain("SLEPT", result.ReceivedOutput);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(20), $"Took {stopwatch.Elapsed} to give up.");
        }

        [Fact]
        public void BurningProcessorTimePastTheLimitIsATimeLimit()
        {
            var exePath = this.CreateExe("BurnPastCpuLimit.exe", BurnCpuSourceCode);

            var request = new ExecutionRequest(exePath)
            {
                Arguments = new[] { "3000" },
                CpuTimeLimit = TimeSpan.FromMilliseconds(300),
                WallClockLimit = TimeSpan.FromSeconds(30),
                MemoryLimitBytes = 64 * 1024 * 1024,
            };

            var result = new RestrictedProcessExecutor().Execute(request);

            Assert.Equal(ProcessExecutionResultType.TimeLimit, result.Type);
        }

        [Fact]
        public void ARequestWithNoLimitsRunsToCompletion()
        {
            var exePath = this.CreateExe("NoLimits.exe", SleepSourceCode);

            var result = new RestrictedProcessExecutor()
                .Execute(new ExecutionRequest(exePath) { Arguments = new[] { "50" } });

            Assert.Equal(ProcessExecutionResultType.Success, result.Type);
            Assert.Equal("SLEPT", result.ReceivedOutput.Trim());
            Assert.Equal(0, result.ExitCode);
        }

        [Fact]
        public void InputLargerThanThePipeBufferIsDelivered()
        {
            // Standard input is written concurrently with the run. If it were written before the process
            // was resumed, anything larger than the pipe buffer would deadlock.
            const string EchoLengthSourceCode = @"using System;
class Program
{
    public static void Main()
    {
        var line = Console.ReadLine();
        Console.WriteLine(line == null ? ""NULL"" : line.Length.ToString());
    }
}";
            var exePath = this.CreateExe("LargeStdin.exe", EchoLengthSourceCode);
            var input = new string('x', 2 * 1024 * 1024);

            var result = new RestrictedProcessExecutor()
                .Execute(UntimedRequest(exePath, input, 20000, 128 * 1024 * 1024));

            Assert.Equal(ProcessExecutionResultType.Success, result.Type);
            Assert.Equal(input.Length.ToString(), result.ReceivedOutput.Trim());
        }

        [Theory]
        [InlineData(1000, false)]
        [InlineData(1001, true)]
        public void OutputIsCutExactlyAtTheCap(int charactersWritten, bool expectedTruncated)
        {
            const string WriteExactlySourceCode = @"using System;
class Program
{
    public static void Main(string[] args)
    {
        Console.Write(new string('a', int.Parse(args[0])));
    }
}";
            var exePath = this.CreateExe("OutputCapBoundary.exe", WriteExactlySourceCode);

            var options = new RestrictedProcessOptions { MaxOutputSize = 1000 };
            var result = new RestrictedProcessExecutor(options).Execute(
                UntimedRequest(exePath, string.Empty, 10000, 64 * 1024 * 1024, new[] { charactersWritten.ToString() }));

            Assert.Equal(expectedTruncated, result.OutputTruncated);
            Assert.Equal(Math.Min(charactersWritten, 1000), result.ReceivedOutput.Length);
            Assert.Equal(
                expectedTruncated ? ProcessExecutionResultType.OutputLimit : ProcessExecutionResultType.Success,
                result.Type);
        }

        [Fact]
        public void AnUnlimitedOutputCapCapturesEverything()
        {
            const string WriteALotSourceCode = @"using System;
class Program
{
    public static void Main()
    {
        Console.Write(new string('b', 300000));
    }
}";
            var exePath = this.CreateExe("UnlimitedOutput.exe", WriteALotSourceCode);

            var options = new RestrictedProcessOptions { MaxOutputSize = 0 };
            var result = new RestrictedProcessExecutor(options)
                .Execute(UntimedRequest(exePath, string.Empty, 20000, 64 * 1024 * 1024));

            Assert.Equal(ProcessExecutionResultType.Success, result.Type);
            Assert.Equal(300000, result.ReceivedOutput.Length);
            Assert.False(result.OutputTruncated);
        }

        [Fact]
        public void AProgramThatNeverReadsItsInputStillFinishes()
        {
            const string IgnoreInputSourceCode = @"using System;
class Program
{
    public static void Main()
    {
        Console.WriteLine(""done"");
    }
}";
            var exePath = this.CreateExe("IgnoresStdin.exe", IgnoreInputSourceCode);

            var result = new RestrictedProcessExecutor()
                .Execute(UntimedRequest(exePath, new string('y', 1024 * 1024), 10000, 64 * 1024 * 1024));

            Assert.Equal(ProcessExecutionResultType.Success, result.Type);
            Assert.Equal("done", result.ReceivedOutput.Trim());
        }

        [Fact]
        public void AMissingExecutableFailsWithTheStepThatFailed()
        {
            var missing = Path.Combine(AppContext.BaseDirectory, "Exe", "does-not-exist-" + Guid.NewGuid().ToString("N") + ".exe");

            var exception = Assert.Throws<SandboxException>(
                () => new RestrictedProcessExecutor().Execute(UntimedRequest(missing, string.Empty, 5000, 32 * 1024 * 1024)));

            Assert.Equal(SandboxStep.CreateProcess, exception.Step);
            Assert.NotEqual(0, exception.NativeErrorCode);
        }

        [Fact]
        public async Task AnAlreadyCancelledTokenStopsBeforeTheProgramFinishes()
        {
            var exePath = this.CreateExe("AlreadyCancelled.exe", SleepSourceCode);

            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();

                var request = new ExecutionRequest(exePath)
                {
                    Arguments = new[] { "30000" },
                    WallClockLimit = TimeSpan.FromSeconds(60),
                };

                var stopwatch = Stopwatch.StartNew();
                var result = await new RestrictedProcessExecutor().ExecuteAsync(request, cancellation.Token);
                stopwatch.Stop();

                Assert.Equal(ProcessExecutionResultType.Cancelled, result.Type);
                Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(20), $"Took {stopwatch.Elapsed}.");
            }
        }

        [Fact]
        public void OneExecutorCanBeUsedRepeatedlyWithoutLeakingStateBetweenRuns()
        {
            const string EchoArgSourceCode = @"using System;
class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine(args[0]);
    }
}";
            var exePath = this.CreateExe("RepeatedRuns.exe", EchoArgSourceCode);
            var executor = new RestrictedProcessExecutor();

            for (var i = 0; i < 8; i++)
            {
                var result = executor.Execute(
                    UntimedRequest(exePath, string.Empty, 10000, 32 * 1024 * 1024, new[] { "run" + i }));

                Assert.Equal(ProcessExecutionResultType.Success, result.Type);
                Assert.Equal("run" + i, result.ReceivedOutput.Trim());
            }
        }

        [Fact]
        public async Task ConcurrentExecutionsDoNotInterfereWithEachOther()
        {
            const string EchoArgSourceCode = @"using System;
using System.Threading;
class Program
{
    public static void Main(string[] args)
    {
        Thread.Sleep(200);
        Console.WriteLine(args[0]);
    }
}";
            var exePath = this.CreateExe("ConcurrentRuns.exe", EchoArgSourceCode);
            var executor = new RestrictedProcessExecutor();

            var running = Enumerable.Range(0, 6)
                .Select(i => executor.ExecuteAsync(
                    UntimedRequest(exePath, string.Empty, 20000, 32 * 1024 * 1024, new[] { "run" + i })))
                .ToArray();

            var results = await Task.WhenAll(running);

            for (var i = 0; i < results.Length; i++)
            {
                Assert.Equal(ProcessExecutionResultType.Success, results[i].Type);
                Assert.Equal("run" + i, results[i].ReceivedOutput.Trim());
            }
        }

        [Fact]
        public void NothingSurvivesAKilledRun()
        {
            var exePath = this.CreateExe("OrphanProbe.exe", SleepSourceCode);

            var request = new ExecutionRequest(exePath)
            {
                Arguments = new[] { "60000" },
                WallClockLimit = TimeSpan.FromSeconds(1),
            };

            var result = new RestrictedProcessExecutor().Execute(request);
            Assert.Equal(ProcessExecutionResultType.TimeLimit, result.Type);

            // The job carries kill-on-close, so by the time Execute has returned there should be nothing
            // left running under that name.
            var survivors = System.Diagnostics.Process.GetProcessesByName("OrphanProbe");
            try
            {
                Assert.Empty(survivors);
            }
            finally
            {
                foreach (var survivor in survivors)
                {
                    survivor.Dispose();
                }
            }
        }

        [Fact]
        public void AnUntrustedIntegrityLevelStillStartsTheProgram()
        {
            // The desktop label is derived from the configured integrity level. While it was hardcoded to
            // Low, an Untrusted process could not attach to its own desktop and never started.
            const string HelloSourceCode = @"using System;
class Program
{
    public static void Main()
    {
        Console.WriteLine(""untrusted-ok"");
    }
}";
            var exePath = this.CreateExe("UntrustedIntegrity.exe", HelloSourceCode);

            var options = new RestrictedProcessOptions { IntegrityLevel = IntegrityLevel.Untrusted };
            var result = new RestrictedProcessExecutor(options)
                .Execute(UntimedRequest(exePath, string.Empty, 10000, 64 * 1024 * 1024));

            Assert.Equal(ProcessExecutionResultType.Success, result.Type);
            Assert.Equal("untrusted-ok", result.ReceivedOutput.Trim());
        }

        [Fact]
        public void ProcessorTimeCoversChildrenAsWellAsTheRootProcess()
        {
            // Accounting comes from the job, not from the root process, so a child's processor time counts
            // even after it has exited.
            var burner = this.CreateExe("ChildBurner.exe", BurnCpuSourceCode);
            var launcher = this.CreateExe("ChildLauncher.exe", LaunchChildSourceCode);

            var options = new RestrictedProcessOptions
            {
                DisallowChildProcesses = false,
                ActiveProcessLimit = 4,
            };

            var result = new RestrictedProcessExecutor(options)
                .Execute(UntimedRequest(launcher, string.Empty, 30000, 128 * 1024 * 1024, new[] { burner }));

            Assert.Contains("child-exited", result.ReceivedOutput);
            Assert.True(
                result.TotalProcessorTime > TimeSpan.FromMilliseconds(500),
                $"Only {result.TotalProcessorTime} of processor time was accounted, so the child's was lost.");
        }

        [Theory]
        [MemberData(nameof(ExitCodes))]
        public void TheExitCodeIsReportedExactly(int exitCode)
        {
            var name = "ExitCode" + (exitCode < 0 ? "Neg" + (-exitCode) : exitCode.ToString()) + ".exe";
            var exePath = this.CreateExe(name, ExitWithCodeSourceCode);

            var result = new RestrictedProcessExecutor()
                .Execute(UntimedRequest(exePath, string.Empty, 10000, 32 * 1024 * 1024, new[] { exitCode.ToString() }));

            Assert.Equal(exitCode, result.ExitCode);
            Assert.Equal(
                exitCode == 0 ? ProcessExecutionResultType.Success : ProcessExecutionResultType.RunTimeError,
                result.Type);
        }
    }
}
