namespace RestrictedProcess.Tests
{
    using System;
    using System.Diagnostics;

    using Xunit;

    public class RestrictedProcessTests : BaseExecutorsTestClass
    {
        private const string ReadInputAndThenOutputSourceCode = @"using System;
class Program
{
    public static void Main()
    {
        var line = Console.ReadLine();
        Console.WriteLine(line);
    }
}";

        private const string Consuming50MbOfMemorySourceCode = @"using System;
using System.Windows.Forms;
class Program
{
    public static void Main()
    {
        var array = new int[50 * 1024 * 1024 / 4];
        for (int i = 0; i < array.Length; i++)
        {
            array[i] = i;
        }
        Console.WriteLine(array[12345]);
    }
}";

        [Fact]
        public void RestrictedProcessShouldStopProgramAfterTimeIsEnded()
        {
            const string TimeLimitSourceCode = @"using System;
using System.Threading;
class Program
{
    public static void Main()
    {
        Thread.Sleep(150);
    }
}";
            var exePath = this.CreateExe("RestrictedProcessShouldStopProgramAfterTimeIsEnded.exe", TimeLimitSourceCode);

            var process = new RestrictedProcessExecutor();
            var result = process.Execute(Request(exePath, string.Empty, 100, 32 * 1024 * 1024));

            Assert.NotNull(result);
            Assert.True(result.Type == ProcessExecutionResultType.TimeLimit);
        }

        [Fact]
        public void RestrictedProcessShouldSendInputDataToProcess()
        {
            var exePath = this.CreateExe("RestrictedProcessShouldSendInputDataToProcess.exe", ReadInputAndThenOutputSourceCode);

            const string InputData = "SomeInputData!!@#$%^&*(\n";
            var process = new RestrictedProcessExecutor();
            var result = process.Execute(UntimedRequest(exePath, InputData, 2000, 32 * 1024 * 1024));

            Assert.NotNull(result);
            Assert.Equal(InputData.Trim(), result.ReceivedOutput.Trim());
        }

        [Fact]
        public void RestrictedProcessShouldWorkWithCyrillic()
        {
            var exePath = this.CreateExe("RestrictedProcessShouldWorkWithCyrillic.exe", ReadInputAndThenOutputSourceCode);

            const string InputData = "Николай\n";
            var process = new RestrictedProcessExecutor();
            var result = process.Execute(UntimedRequest(exePath, InputData, 2000, 32 * 1024 * 1024));

            Assert.NotNull(result);
            Assert.Equal(InputData.Trim(), result.ReceivedOutput.Trim());
        }

        [Fact]
        public void RestrictedProcessShouldOutputProperLengthForCyrillicText()
        {
            const string ReadInputAndThenOutputTheLengthSourceCode = @"using System;
class Program
{
    public static void Main()
    {
        var line = Console.ReadLine();
        Console.WriteLine(line.Length);
    }
}";
            var exePath = this.CreateExe("RestrictedProcessShouldOutputProperLengthForCyrillicText.exe", ReadInputAndThenOutputTheLengthSourceCode);

            const string InputData = "Николай\n";
            var process = new RestrictedProcessExecutor();
            var result = process.Execute(UntimedRequest(exePath, InputData, 2000, 32 * 1024 * 1024));

            Assert.NotNull(result);
            Assert.Equal("7", result.ReceivedOutput.Trim());
        }

        [Fact]
        public void RestrictedProcessShouldReceiveCyrillicText()
        {
            const string ReadInputAndThenCheckTheTextToContainCyrillicLettersSourceCode = @"using System;
class Program
{
    public static void Main()
    {
        var line = Console.ReadLine();
        Console.WriteLine((line.Contains(""а"") || line.Contains(""е"")));
    }
}";
            var exePath = this.CreateExe("RestrictedProcessShouldReceiveCyrillicText.exe", ReadInputAndThenCheckTheTextToContainCyrillicLettersSourceCode);

            const string InputData = "абвгдежзийклмнопрстуфхцчшщъьюя\n";
            var process = new RestrictedProcessExecutor();
            var result = process.Execute(UntimedRequest(exePath, InputData, 2000, 32 * 1024 * 1024));

            Assert.NotNull(result);
            Assert.Equal("True", result.ReceivedOutput.Trim());
        }

        [Fact]
        public void RestrictedProcessShouldNotBlockWhenEnterEndlessLoop()
        {
            const string EndlessLoopSourceCode = @"using System;
class Program
{
    public static void Main()
    {
        while(true) { }
    }
}";
            var exePath = this.CreateExe("RestrictedProcessShouldNotBlockWhenEnterEndlessLoop.exe", EndlessLoopSourceCode);

            var process = new RestrictedProcessExecutor();
            var result = process.Execute(Request(exePath, string.Empty, 50, 32 * 1024 * 1024));

            Assert.NotNull(result);
            Assert.True(result.Type == ProcessExecutionResultType.TimeLimit);
        }

        [Fact]
        public void RestrictedProcessStandardErrorContentShouldContainExceptions()
        {
            const string ThrowExceptionSourceCode = @"using System;
using System.Windows.Forms;
class Program
{
    public static void Main()
    {
        throw new Exception(""Exception message!"");
    }
}";
            var exePath = this.CreateExe("RestrictedProcessShouldStandardErrorContentShouldContainExceptions.exe", ThrowExceptionSourceCode);

            var process = new RestrictedProcessExecutor();
            var result = process.Execute(UntimedRequest(exePath, string.Empty, 500, 32 * 1024 * 1024));

            Assert.NotNull(result);
            Assert.True(result.Type == ProcessExecutionResultType.RunTimeError, "No exception is thrown!");
            Assert.Contains("Exception message!", result.ErrorOutput);
        }

        [Fact]
        public void RestrictedProcessShouldReturnCorrectAmountOfUsedMemory()
        {
            var exePath = this.CreateExe("RestrictedProcessShouldReturnCorrectAmountOfUsedMemory.exe", Consuming50MbOfMemorySourceCode);

            var process = new RestrictedProcessExecutor();
            var result = process.Execute(UntimedRequest(exePath, string.Empty, 5000, 100 * 1024 * 1024));

            Console.WriteLine(result.MemoryUsed);

            Assert.NotNull(result);
            Assert.True(result.MemoryUsed > 50 * 1024 * 1024);
        }

        [Fact]
        public void RestrictedProcessShouldReturnMemoryLimitWhenNeeded()
        {
            var exePath = this.CreateExe("RestrictedProcessShouldReturnMemoryLimitWhenNeeded.exe", Consuming50MbOfMemorySourceCode);

            var process = new RestrictedProcessExecutor();
            var result = process.Execute(UntimedRequest(exePath, string.Empty, 5000, 30 * 1024 * 1024));

            Console.WriteLine(result.MemoryUsed);

            Assert.NotNull(result);
            Assert.True(result.Type == ProcessExecutionResultType.MemoryLimit);
        }

        [Fact]
        public void RestrictedProcessShouldStopAFloodingProcess()
        {
            const string FloodingSourceCode = @"using System;
class Program
{
    public static void Main()
    {
        var line = new string('a', 1000);
        while (true)
        {
            Console.WriteLine(line);
        }
    }
}";
            var exePath = this.CreateExe("RestrictedProcessShouldStopAFloodingProcess.exe", FloodingSourceCode);

            const long MaxOutputSize = 1024 * 1024;
            var options = new RestrictedProcessOptions { MaxOutputSize = MaxOutputSize };
            var result = new RestrictedProcessExecutor(options).Execute(Request(exePath, string.Empty, 5000, 32 * 1024 * 1024));

            Assert.NotNull(result);
            Assert.Equal(ProcessExecutionResultType.OutputLimit, result.Type);
            Assert.True(result.ReceivedOutput.Length <= MaxOutputSize, $"Output was not bounded: {result.ReceivedOutput.Length} characters.");

            // The flood was cut short well before the time limit, not left to run out the clock.
            Assert.True(result.TimeWorked.TotalMilliseconds < 5000, $"The process ran for {result.TimeWorked.TotalMilliseconds} ms.");
        }

        [Fact]
        public void RestrictedProcessShouldReportNonZeroExitCodeAsRunTimeError()
        {
            const string ExitWithCodeSourceCode = @"using System;
class Program
{
    public static void Main()
    {
        Environment.Exit(1);
    }
}";
            var exePath = this.CreateExe("RestrictedProcessShouldReportNonZeroExitCodeAsRunTimeError.exe", ExitWithCodeSourceCode);

            var result = new RestrictedProcessExecutor().Execute(UntimedRequest(exePath, string.Empty, 2000, 32 * 1024 * 1024));

            Assert.NotNull(result);
            Assert.Equal(ProcessExecutionResultType.RunTimeError, result.Type);
            Assert.Equal(1, result.ExitCode);
        }

        [Fact]
        public void RestrictedProcessShouldRunWithConfiguredPriorityClass()
        {
            const string PrintPriorityClassSourceCode = @"using System;
using System.Diagnostics;
class Program
{
    public static void Main()
    {
        Console.WriteLine(Process.GetCurrentProcess().PriorityClass);
    }
}";
            var exePath = this.CreateExe("RestrictedProcessShouldRunWithConfiguredPriorityClass.exe", PrintPriorityClassSourceCode);

            var defaultResult = new RestrictedProcessExecutor().Execute(UntimedRequest(exePath, string.Empty, 2000, 32 * 1024 * 1024));
            Assert.Equal(ProcessExecutionResultType.Success, defaultResult.Type);
            Assert.Equal("High", defaultResult.ReceivedOutput.Trim());

            var options = new RestrictedProcessOptions { PriorityClass = ProcessPriorityClass.Normal };
            var normalResult = new RestrictedProcessExecutor(options).Execute(UntimedRequest(exePath, string.Empty, 2000, 32 * 1024 * 1024));
            Assert.Equal(ProcessExecutionResultType.Success, normalResult.Type);
            Assert.Equal("Normal", normalResult.ReceivedOutput.Trim());
        }

        [Fact]
        public void RestrictedProcessShouldRespectProcessorAffinity()
        {
            const string PrintAffinitySourceCode = @"using System;
using System.Diagnostics;
class Program
{
    public static void Main()
    {
        Console.WriteLine(Process.GetCurrentProcess().ProcessorAffinity.ToInt64());
    }
}";
            var exePath = this.CreateExe("RestrictedProcessShouldRespectProcessorAffinity.exe", PrintAffinitySourceCode);

            var options = new RestrictedProcessOptions { ProcessorAffinityMask = (UIntPtr)0x1 };
            var result = new RestrictedProcessExecutor(options).Execute(UntimedRequest(exePath, string.Empty, 2000, 32 * 1024 * 1024));

            Assert.Equal(ProcessExecutionResultType.Success, result.Type);
            Assert.Equal("1", result.ReceivedOutput.Trim());
        }

        [Fact]
        public void RestrictedProcessShouldRespectCpuRateLimit()
        {
            const string BusyLoopSourceCode = @"using System;
using System.Diagnostics;
class Program
{
    public static void Main()
    {
        var stopwatch = Stopwatch.StartNew();
        long counter = 0;
        while (stopwatch.ElapsedMilliseconds < 1500)
        {
            counter++;
        }
        Console.WriteLine(counter);
    }
}";
            var exePath = this.CreateExe("RestrictedProcessShouldRespectCpuRateLimit.exe", BusyLoopSourceCode);

            // The CPU rate is a percentage of the whole machine's capacity, so to throttle a single
            // thread the cap must be below one core's share (100 / ProcessorCount). Cap the job to
            // roughly 0.4 of a core regardless of the core count.
            var percent = Math.Max(1, 40 / Environment.ProcessorCount);

            var options = new RestrictedProcessOptions { CpuRateLimitPercent = percent };
            var result = new RestrictedProcessExecutor(options).Execute(Request(exePath, string.Empty, 5000, 32 * 1024 * 1024));
            Assert.Equal(ProcessExecutionResultType.Success, result.Type);

            // The hard cap is an absolute upper bound on CPU time regardless of other load: a busy
            // loop throttled to well under one core must burn far less CPU time than the wall-clock
            // time it ran for (an unthrottled single thread would be close to 100%).
            var cpuMilliseconds = result.TotalProcessorTime.TotalMilliseconds;
            var wallMilliseconds = result.TimeWorked.TotalMilliseconds;
            Assert.True(
                cpuMilliseconds < 0.65 * wallMilliseconds,
                $"CPU time {cpuMilliseconds} ms was not throttled below 65% of wall time {wallMilliseconds} ms.");
        }
    }
}
