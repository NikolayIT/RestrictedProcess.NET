namespace RestrictedProcess.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;

    using Xunit;

    public abstract class BaseExecutorsTestClass
    {
        // The compiled test programs target the .NET Framework built into Windows,
        // so they are standalone executables that can be started directly in the sandbox.
        private static readonly string DotNetFrameworkDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "Microsoft.NET",
            "Framework64",
            "v4.0.30319");

        private readonly string exeDirectory = Path.Combine(AppContext.BaseDirectory, "Exe");

        /// <summary>
        /// Builds an execution request from the four values the tests care about. The library keeps
        /// processor time and wall-clock time apart; these tests pass a processor time limit and let the
        /// wall-clock deadline be derived from it, which is the behaviour they were written against.
        /// </summary>
        public static ExecutionRequest Request(
            string fileName,
            string input,
            int timeLimitMilliseconds,
            long memoryLimitBytes,
            IReadOnlyList<string>? arguments = null)
        {
            return new ExecutionRequest(fileName)
            {
                Input = input,
                CpuTimeLimit = TimeSpan.FromMilliseconds(timeLimitMilliseconds),
                MemoryLimitBytes = memoryLimitBytes,
                Arguments = arguments,
            };
        }

        /// <summary>
        /// A request for tests that are measuring something other than time. It keeps the processor time
        /// limit but gives the run a generous wall-clock deadline, so a loaded build machine cannot turn
        /// the assertion into a spurious time limit.
        /// </summary>
        public static ExecutionRequest UntimedRequest(
            string fileName,
            string input,
            int timeLimitMilliseconds,
            long memoryLimitBytes,
            IReadOnlyList<string>? arguments = null)
        {
            var request = Request(fileName, input, timeLimitMilliseconds, memoryLimitBytes, arguments);
            request.WallClockLimit = TimeSpan.FromSeconds(30);
            return request;
        }

        public string CreateExe(string exeName, string sourceString)
        {
            Directory.CreateDirectory(this.exeDirectory);
            var outputExePath = Path.Combine(this.exeDirectory, exeName);
            if (File.Exists(outputExePath))
            {
                File.Delete(outputExePath);
            }

            var compilation = CSharpCompilation.Create(
                Path.GetFileNameWithoutExtension(exeName),
                new[] { CSharpSyntaxTree.ParseText(sourceString) },
                new[]
                {
                    MetadataReference.CreateFromFile(Path.Combine(DotNetFrameworkDirectory, "mscorlib.dll")),
                    MetadataReference.CreateFromFile(Path.Combine(DotNetFrameworkDirectory, "System.dll")),
                    MetadataReference.CreateFromFile(Path.Combine(DotNetFrameworkDirectory, "System.Windows.Forms.dll")),
                },
                new CSharpCompilationOptions(OutputKind.ConsoleApplication));

            var emitResult = compilation.Emit(outputExePath);
            foreach (var diagnostic in emitResult.Diagnostics)
            {
                Console.WriteLine(diagnostic.ToString());
            }

            Assert.True(emitResult.Success, "Code compilation contains errors!");
            return outputExePath;
        }
    }
}
