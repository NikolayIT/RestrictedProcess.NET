namespace RestrictedProcess.Tests
{
    using System;

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
            var result = process.Execute(exePath, string.Empty, 100, 32 * 1024 * 1024);

            Assert.NotNull(result);
            Assert.True(result.Type == ProcessExecutionResultType.TimeLimit);
        }

        [Fact]
        public void RestrictedProcessShouldSendInputDataToProcess()
        {
            var exePath = this.CreateExe("RestrictedProcessShouldSendInputDataToProcess.exe", ReadInputAndThenOutputSourceCode);

            const string InputData = "SomeInputData!!@#$%^&*(\n";
            var process = new RestrictedProcessExecutor();
            var result = process.Execute(exePath, InputData, 2000, 32 * 1024 * 1024);

            Assert.NotNull(result);
            Assert.Equal(InputData.Trim(), result.ReceivedOutput.Trim());
        }

        [Fact]
        public void RestrictedProcessShouldWorkWithCyrillic()
        {
            var exePath = this.CreateExe("RestrictedProcessShouldWorkWithCyrillic.exe", ReadInputAndThenOutputSourceCode);

            const string InputData = "Николай\n";
            var process = new RestrictedProcessExecutor();
            var result = process.Execute(exePath, InputData, 2000, 32 * 1024 * 1024);

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
            var result = process.Execute(exePath, InputData, 2000, 32 * 1024 * 1024);

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
            var result = process.Execute(exePath, InputData, 2000, 32 * 1024 * 1024);

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
            var result = process.Execute(exePath, string.Empty, 50, 32 * 1024 * 1024);

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
            var result = process.Execute(exePath, string.Empty, 500, 32 * 1024 * 1024);

            Assert.NotNull(result);
            Assert.True(result.Type == ProcessExecutionResultType.RunTimeError, "No exception is thrown!");
            Assert.Contains("Exception message!", result.ErrorOutput);
        }

        [Fact]
        public void RestrictedProcessShouldReturnCorrectAmountOfUsedMemory()
        {
            var exePath = this.CreateExe("RestrictedProcessShouldReturnCorrectAmountOfUsedMemory.exe", Consuming50MbOfMemorySourceCode);

            var process = new RestrictedProcessExecutor();
            var result = process.Execute(exePath, string.Empty, 5000, 100 * 1024 * 1024);

            Console.WriteLine(result.MemoryUsed);

            Assert.NotNull(result);
            Assert.True(result.MemoryUsed > 50 * 1024 * 1024);
        }

        [Fact]
        public void RestrictedProcessShouldReturnMemoryLimitWhenNeeded()
        {
            var exePath = this.CreateExe("RestrictedProcessShouldReturnMemoryLimitWhenNeeded.exe", Consuming50MbOfMemorySourceCode);

            var process = new RestrictedProcessExecutor();
            var result = process.Execute(exePath, string.Empty, 5000, 30 * 1024 * 1024);

            Console.WriteLine(result.MemoryUsed);

            Assert.NotNull(result);
            Assert.True(result.Type == ProcessExecutionResultType.MemoryLimit);
        }
    }
}
