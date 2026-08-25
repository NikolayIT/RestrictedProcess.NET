namespace RestrictedProcess.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text;

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

        /// <summary>
        /// Whether the system ANSI code page can represent the given text. The sandbox reads and writes
        /// redirected standard IO in that code page by default, because that is what a console child
        /// writes; on a machine whose code page is 1252 a Cyrillic string cannot survive the round trip in
        /// either direction, and the child emits question marks before the sandbox ever sees the bytes.
        /// Tests that depend on the host locale check this rather than assuming it.
        /// </summary>
        public static bool AnsiCodePageCanRepresent(string text)
        {
            var codePage = (int)NativeMethods.GetACP();
            var encoding = CodePagesEncodingProvider.Instance.GetEncoding(codePage) ?? Encoding.GetEncoding(codePage);
            return encoding.GetString(encoding.GetBytes(text)) == text;
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

        private static class NativeMethods
        {
            [DllImport("kernel32.dll")]
            internal static extern uint GetACP();
        }
    }
}
