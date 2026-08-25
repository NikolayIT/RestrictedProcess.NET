namespace RestrictedProcess.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.InteropServices;

    using RestrictedProcess.Process;

    using Xunit;

    /// <summary>
    /// The escaper is checked against the real parser rather than against a hand-written expectation:
    /// whatever <c>CommandLineToArgvW</c> makes of the string is what the child's own startup code will
    /// make of it, so a round trip through it is the only assertion that actually means anything.
    /// </summary>
    public class CommandLineTests
    {
        public static TheoryData<string[]> Arguments =>
            new TheoryData<string[]>
            {
                new[] { "plain" },
                new[] { "with space" },
                new[] { "  leading and trailing  " },
                new[] { "with\ttab" },
                new[] { "with \"quote\"" },
                new[] { "\"fully quoted\"" },
                new[] { "ends with quote\"" },
                new[] { @"trailing\backslash\" },
                new[] { @"two\\backslashes\\" },
                new[] { @"backslash before quote \""" },
                new[] { @"\\\""odd run before quote" },
                new[] { @"C:\Program Files\app\run.exe" },
                new[] { string.Empty },
                new[] { "a", "b", "c" },
                new[] { "a b", string.Empty, "c\"d", @"e\", "f g h" },
                new[] { "unicode Николай ✓" },
                new[] { "&|<>^", "%PATH%", "$(echo)" },
            };

        [Theory]
        [MemberData(nameof(Arguments))]
        public void EscapedArgumentsRoundTripThroughTheWin32Parser(string[] arguments)
        {
            const string FileName = @"C:\a directory\a program.exe";

            var commandLine = CommandLine.Build(FileName, arguments);
            var parsed = Parse(commandLine);

            // argv[0] is the program itself, then one entry per argument, in order and unaltered.
            Assert.Equal(arguments.Length + 1, parsed.Count);
            Assert.Equal(FileName, parsed[0]);
            for (var i = 0; i < arguments.Length; i++)
            {
                Assert.Equal(arguments[i], parsed[i + 1]);
            }
        }

        [Fact]
        public void APathWithSpacesIsQuotedSoNoEarlierPrefixCanWin()
        {
            // Unquoted, CreateProcess would try C:\Program.exe first when no application name is given.
            var commandLine = CommandLine.Build(@"C:\Program Files\app\run.exe", null);

            Assert.StartsWith("\"", commandLine, StringComparison.Ordinal);
            Assert.Equal("\"C:\\Program Files\\app\\run.exe\"", commandLine);
        }

        [Fact]
        public void APathWithoutSpacesIsLeftAlone()
        {
            Assert.Equal(@"C:\app\run.exe", CommandLine.Build(@"C:\app\run.exe", null));
        }

        [Fact]
        public void ANullArgumentIsTreatedAsEmpty()
        {
            var commandLine = CommandLine.Build("prog.exe", new string[] { null! });
            var parsed = Parse(commandLine);

            Assert.Equal(2, parsed.Count);
            Assert.Equal(string.Empty, parsed[1]);
        }

        private static IReadOnlyList<string> Parse(string commandLine)
        {
            var argv = NativeMethods.CommandLineToArgv(commandLine, out var count);
            Assert.NotEqual(IntPtr.Zero, argv);

            try
            {
                var result = new List<string>(count);
                for (var i = 0; i < count; i++)
                {
                    var pointer = Marshal.ReadIntPtr(argv, i * IntPtr.Size);
                    result.Add(Marshal.PtrToStringUni(pointer) ?? string.Empty);
                }

                return result;
            }
            finally
            {
                NativeMethods.LocalFree(argv);
            }
        }

        private static class NativeMethods
        {
            [DllImport("shell32.dll", EntryPoint = "CommandLineToArgvW", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern IntPtr CommandLineToArgv(string commandLine, out int argumentCount);

            [DllImport("kernel32.dll")]
            internal static extern IntPtr LocalFree(IntPtr memory);
        }
    }
}
