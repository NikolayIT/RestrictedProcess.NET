namespace RestrictedProcess.Tests
{
    using System;
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
