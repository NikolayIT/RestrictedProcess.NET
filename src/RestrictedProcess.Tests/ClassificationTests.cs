namespace RestrictedProcess.Tests
{
    using System;

    using Xunit;

    /// <summary>
    /// Which verdict wins when a run trips more than one condition at once. A judge shows one outcome per
    /// submission, so the precedence is part of the contract and not an implementation detail.
    /// </summary>
    public class ClassificationTests : BaseExecutorsTestClass
    {
        private const string NoisyBurnSourceCode = @"using System;
using System.Diagnostics;
class Program
{
    public static void Main(string[] args)
    {
        // Burn a fixed amount of *processor* time, not wall time. Spinning for a wall-clock duration
        // accumulates however much CPU the machine happens to spare, which on a loaded build agent can be
        // a fraction of it - and then an assertion about processor time fails for no good reason.
        Console.Error.WriteLine(""a warning on standard error"");
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

        private const string NoisyAllocateSourceCode = @"using System;
class Program
{
    public static void Main()
    {
        Console.Error.WriteLine(""a warning on standard error"");
        var blocks = new System.Collections.Generic.List<byte[]>();
        for (var i = 0; i < 120; i++)
        {
            var block = new byte[1024 * 1024];
            for (var j = 0; j < block.Length; j += 4096) { block[j] = 1; }
            blocks.Add(block);
        }

        Console.WriteLine(blocks.Count);
    }
}";

        [Fact]
        public void ExceedingTheProcessorTimeLimitOutranksOutputOnStandardError()
        {
            // A program can print a warning and still be over its limit. Reporting that as a runtime error
            // hides the reason it was stopped, and it is inconsistent with the memory limit, which already
            // outranks standard error.
            var exePath = this.CreateExe("NoisyTimeLimit.exe", NoisyBurnSourceCode);

            var request = new ExecutionRequest(exePath)
            {
                Arguments = new[] { "3000" },
                CpuTimeLimit = TimeSpan.FromMilliseconds(300),
                WallClockLimit = TimeSpan.FromSeconds(30),
                MemoryLimitBytes = 128 * 1024 * 1024,
            };

            var result = new RestrictedProcessExecutor().Execute(request);

            Assert.NotEmpty(result.ErrorOutput);
            Assert.Equal(ProcessExecutionResultType.TimeLimit, result.Type);
        }

        [Fact]
        public void ExceedingTheWallClockDeadlineOutranksOutputOnStandardError()
        {
            var exePath = this.CreateExe("NoisyWallClock.exe", NoisyBurnSourceCode);

            var request = new ExecutionRequest(exePath)
            {
                Arguments = new[] { "30000" },
                CpuTimeLimit = TimeSpan.FromSeconds(60),
                WallClockLimit = TimeSpan.FromSeconds(1),
                MemoryLimitBytes = 128 * 1024 * 1024,
            };

            var result = new RestrictedProcessExecutor().Execute(request);

            Assert.Equal(ProcessExecutionResultType.TimeLimit, result.Type);
        }

        [Fact]
        public void ExceedingTheMemoryLimitOutranksOutputOnStandardError()
        {
            var exePath = this.CreateExe("NoisyMemoryLimit.exe", NoisyAllocateSourceCode);

            var result = new RestrictedProcessExecutor()
                .Execute(UntimedRequest(exePath, string.Empty, 30000, 32 * 1024 * 1024));

            Assert.NotEmpty(result.ErrorOutput);
            Assert.Equal(ProcessExecutionResultType.MemoryLimit, result.Type);
        }

        [Fact]
        public void OutputOnStandardErrorAloneIsARuntimeError()
        {
            const string WarnOnlySourceCode = @"using System;
class Program
{
    public static void Main()
    {
        Console.Error.WriteLine(""something went wrong"");
        Console.WriteLine(""but I finished"");
    }
}";
            var exePath = this.CreateExe("WarnOnly.exe", WarnOnlySourceCode);

            var result = new RestrictedProcessExecutor()
                .Execute(UntimedRequest(exePath, string.Empty, 10000, 32 * 1024 * 1024));

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(ProcessExecutionResultType.RunTimeError, result.Type);
        }

        [Fact]
        public void ANonZeroExitCodeCanBeToleratedWhenTheCallerAsksForIt()
        {
            const string ExitOneSourceCode = @"using System;
class Program
{
    public static void Main()
    {
        Console.WriteLine(""finished"");
        Environment.Exit(3);
    }
}";
            var exePath = this.CreateExe("ToleratedExitCode.exe", ExitOneSourceCode);

            var options = new RestrictedProcessOptions { TreatNonZeroExitCodeAsRunTimeError = false };
            var result = new RestrictedProcessExecutor(options)
                .Execute(UntimedRequest(exePath, string.Empty, 10000, 32 * 1024 * 1024));

            Assert.Equal(3, result.ExitCode);
            Assert.Equal(ProcessExecutionResultType.Success, result.Type);
        }

        [Fact]
        public void FloodingStandardErrorIsAnOutputLimit()
        {
            const string FloodErrorSourceCode = @"using System;
class Program
{
    public static void Main()
    {
        var line = new string('e', 1000);
        while (true) { Console.Error.WriteLine(line); }
    }
}";
            var exePath = this.CreateExe("FloodStandardError.exe", FloodErrorSourceCode);

            var options = new RestrictedProcessOptions { MaxErrorSize = 64 * 1024 };
            var result = new RestrictedProcessExecutor(options)
                .Execute(UntimedRequest(exePath, string.Empty, 20000, 64 * 1024 * 1024));

            Assert.True(result.ErrorTruncated);
            Assert.Equal(ProcessExecutionResultType.OutputLimit, result.Type);
            Assert.True(result.ErrorOutput.Length <= 64 * 1024);
        }

        [Theory]
        [InlineData(MemoryMetric.PeakCommit)]
        [InlineData(MemoryMetric.PeakWorkingSet)]
        [InlineData(MemoryMetric.Max)]
        public void EveryMemoryMetricReportsSomethingPlausible(MemoryMetric metric)
        {
            const string AllocateFiftyMegabytesSourceCode = @"using System;
class Program
{
    public static void Main()
    {
        var array = new int[50 * 1024 * 1024 / 4];
        for (var i = 0; i < array.Length; i++) { array[i] = i; }
        Console.WriteLine(array[12345]);
    }
}";
            var exePath = this.CreateExe("MemoryMetric" + metric + ".exe", AllocateFiftyMegabytesSourceCode);

            var options = new RestrictedProcessOptions { MemoryMetric = metric };
            var result = new RestrictedProcessExecutor(options)
                .Execute(UntimedRequest(exePath, string.Empty, 30000, 512 * 1024 * 1024));

            Assert.Equal(ProcessExecutionResultType.Success, result.Type);
            Assert.True(result.PeakCommitBytes > 50 * 1024 * 1024, $"commit was {result.PeakCommitBytes}");
            Assert.True(result.PeakWorkingSetBytes > 0, $"working set was {result.PeakWorkingSetBytes}");

            var expected = metric switch
            {
                MemoryMetric.PeakWorkingSet => result.PeakWorkingSetBytes,
                MemoryMetric.Max => Math.Max(result.PeakCommitBytes, result.PeakWorkingSetBytes),
                _ => result.PeakCommitBytes,
            };

            Assert.Equal(expected, result.MemoryUsed);
        }

        [Fact]
        public void TheWallClockDeadlineIsDerivedFromTheProcessorTimeLimitWhenNotGiven()
        {
            // With only a processor time limit set, the deadline is WallClockWaitMultiplier times it, which
            // is what stops a sleeping program from running forever.
            const string SleepForeverSourceCode = @"using System;
using System.Threading;
class Program
{
    public static void Main()
    {
        Thread.Sleep(60000);
    }
}";
            var exePath = this.CreateExe("DerivedDeadline.exe", SleepForeverSourceCode);

            var options = new RestrictedProcessOptions { WallClockWaitMultiplier = 2.0 };
            var request = new ExecutionRequest(exePath) { CpuTimeLimit = TimeSpan.FromMilliseconds(500) };

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = new RestrictedProcessExecutor(options).Execute(request);
            stopwatch.Stop();

            Assert.Equal(ProcessExecutionResultType.TimeLimit, result.Type);
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(20),
                $"The derived deadline did not apply; the run took {stopwatch.Elapsed}.");
        }
    }
}
