namespace RestrictedProcess.Tests
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    using Xunit;

    /// <summary>
    /// The unsandboxed executor. It shares the interface, the result shape and the verdict rules with the
    /// sandboxed one, so it needs the same contract tests - and one that demonstrates the difference that
    /// matters: it really does not isolate anything.
    /// </summary>
    public class StandardProcessExecutorTests : BaseExecutorsTestClass
    {
        private const string EchoInputSourceCode = @"using System;
class Program
{
    public static void Main()
    {
        Console.WriteLine(Console.ReadLine());
    }
}";

        private const string CreateFileSourceCode = @"using System;
using System.IO;
class Program
{
    public static void Main(string[] args)
    {
        try
        {
            File.WriteAllText(args[0], ""written"");
            Console.WriteLine(""WROTE"");
        }
        catch (Exception)
        {
            Console.WriteLine(""DENIED"");
        }
    }
}";

        [Fact]
        public void ItRunsAProgramAndCapturesItsOutput()
        {
            var exePath = this.CreateExe("StandardEcho.exe", EchoInputSourceCode);

            var result = new StandardProcessExecutor()
                .Execute(UntimedRequest(exePath, "hello standard", 10000, 32 * 1024 * 1024));

            Assert.Equal(ProcessExecutionResultType.Success, result.Type);
            Assert.Equal("hello standard", result.ReceivedOutput.Trim());
            Assert.Equal(0, result.ExitCode);
        }

        [Fact]
        public void ItStopsAProgramThatOverrunsTheWallClockDeadline()
        {
            const string SleepSourceCode = @"using System;
using System.Threading;
class Program
{
    public static void Main()
    {
        Thread.Sleep(30000);
        Console.WriteLine(""SLEPT"");
    }
}";
            var exePath = this.CreateExe("StandardSleep.exe", SleepSourceCode);

            var request = new ExecutionRequest(exePath) { WallClockLimit = TimeSpan.FromSeconds(1) };

            var stopwatch = Stopwatch.StartNew();
            var result = new StandardProcessExecutor().Execute(request);
            stopwatch.Stop();

            Assert.Equal(ProcessExecutionResultType.TimeLimit, result.Type);
            Assert.DoesNotContain("SLEPT", result.ReceivedOutput);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(20), $"Took {stopwatch.Elapsed}.");
        }

        [Fact]
        public void ItReportsANonZeroExitCodeAsARuntimeError()
        {
            const string ExitSourceCode = @"using System;
class Program
{
    public static void Main()
    {
        Environment.Exit(7);
    }
}";
            var exePath = this.CreateExe("StandardExitCode.exe", ExitSourceCode);

            var result = new StandardProcessExecutor()
                .Execute(UntimedRequest(exePath, string.Empty, 10000, 32 * 1024 * 1024));

            Assert.Equal(7, result.ExitCode);
            Assert.Equal(ProcessExecutionResultType.RunTimeError, result.Type);
        }

        [Fact]
        public void ItReportsOutputOnStandardErrorAsARuntimeError()
        {
            const string WarnSourceCode = @"using System;
class Program
{
    public static void Main()
    {
        Console.Error.WriteLine(""bad news"");
    }
}";
            var exePath = this.CreateExe("StandardStderr.exe", WarnSourceCode);

            var result = new StandardProcessExecutor()
                .Execute(UntimedRequest(exePath, string.Empty, 10000, 32 * 1024 * 1024));

            Assert.Contains("bad news", result.ErrorOutput);
            Assert.Equal(ProcessExecutionResultType.RunTimeError, result.Type);
        }

        [Fact]
        public void ItCapsOutputTheSameWayTheSandboxDoes()
        {
            const string FloodSourceCode = @"using System;
class Program
{
    public static void Main()
    {
        var line = new string('a', 1000);
        while (true) { Console.WriteLine(line); }
    }
}";
            var exePath = this.CreateExe("StandardFlood.exe", FloodSourceCode);

            var options = new RestrictedProcessOptions { MaxOutputSize = 64 * 1024 };
            var request = new ExecutionRequest(exePath) { WallClockLimit = TimeSpan.FromSeconds(20) };

            var result = new StandardProcessExecutor(options).Execute(request);

            Assert.True(result.OutputTruncated);
            Assert.True(result.ReceivedOutput.Length <= 64 * 1024);
            Assert.Equal(ProcessExecutionResultType.OutputLimit, result.Type);
        }

        [Fact]
        public async Task ItHonoursCancellation()
        {
            const string SleepSourceCode = @"using System;
using System.Threading;
class Program
{
    public static void Main()
    {
        Thread.Sleep(30000);
    }
}";
            var exePath = this.CreateExe("StandardCancel.exe", SleepSourceCode);

            using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300)))
            {
                var request = new ExecutionRequest(exePath) { WallClockLimit = TimeSpan.FromSeconds(60) };

                var stopwatch = Stopwatch.StartNew();
                var result = await new StandardProcessExecutor().ExecuteAsync(request, cancellation.Token);
                stopwatch.Stop();

                Assert.Equal(ProcessExecutionResultType.Cancelled, result.Type);
                Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(20), $"Took {stopwatch.Elapsed}.");
            }
        }

        [Fact]
        public void ItReportsProcessorTimeAndMemory()
        {
            const string BurnSourceCode = @"using System;
using System.Diagnostics;
class Program
{
    public static void Main()
    {
        var stopwatch = Stopwatch.StartNew();
        long counter = 0;
        while (stopwatch.ElapsedMilliseconds < 400) { counter++; }
        Console.WriteLine(counter);
    }
}";
            var exePath = this.CreateExe("StandardBurn.exe", BurnSourceCode);

            var result = new StandardProcessExecutor()
                .Execute(UntimedRequest(exePath, string.Empty, 30000, 256 * 1024 * 1024));

            Assert.Equal(ProcessExecutionResultType.Success, result.Type);
            Assert.True(result.TotalProcessorTime > TimeSpan.FromMilliseconds(100), $"cpu {result.TotalProcessorTime}");
            Assert.True(result.PeakWorkingSetBytes > 0, $"working set {result.PeakWorkingSetBytes}");
            Assert.True(result.TimeWorked > TimeSpan.Zero, $"elapsed {result.TimeWorked}");
        }

        [Fact]
        public void ItReallyDoesNotSandboxAnything()
        {
            // The contrast that gives the sandbox its point: the same program, writing to the same place,
            // succeeds unsandboxed and is refused under the sandbox.
            var exePath = this.CreateExe("StandardNoIsolation.exe", CreateFileSourceCode);
            var target = Path.Combine(Path.GetTempPath(), "rp_unsandboxed_" + Guid.NewGuid().ToString("N") + ".txt");

            try
            {
                var unsandboxed = new StandardProcessExecutor()
                    .Execute(UntimedRequest(exePath, string.Empty, 10000, 32 * 1024 * 1024, new[] { target }));

                Assert.Equal("WROTE", unsandboxed.ReceivedOutput.Trim());
                Assert.True(File.Exists(target));
                File.Delete(target);

                var sandboxed = new RestrictedProcessExecutor()
                    .Execute(UntimedRequest(exePath, string.Empty, 10000, 32 * 1024 * 1024, new[] { target }));

                Assert.Equal("DENIED", sandboxed.ReceivedOutput.Trim());
                Assert.False(File.Exists(target));
            }
            finally
            {
                if (File.Exists(target))
                {
                    File.Delete(target);
                }
            }
        }
    }
}
