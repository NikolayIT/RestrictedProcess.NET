namespace RestrictedProcess.Tests
{
    using System;
    using System.Runtime.InteropServices;
    using System.Windows.Forms;

    using Xunit;

    public class RestrictedProcessSecurityTests : BaseExecutorsTestClass
    {
        private const uint WaitObject0 = 0;

        [Fact]
        public void RestrictedProcessShouldNotBeAbleToCreateFiles()
        {
            const string CreateFileSourceCode = @"using System;
using System.IO;
class Program
{
    public static void Main()
    {
        File.OpenWrite(""test.txt"");
    }
}";
            var exePath = this.CreateExe("RestrictedProcessShouldNotBeAbleToCreateFiles.exe", CreateFileSourceCode);

            var process = new RestrictedProcessExecutor();
            var result = process.Execute(exePath, string.Empty, 1000, 32 * 1024 * 1024);

            Assert.NotNull(result);
            Assert.True(result.Type == ProcessExecutionResultType.RunTimeError, "No exception is thrown!");
        }

        [StaFact]
        public void RestrictedProcessShouldNotBeAbleToReadClipboard()
        {
            const string ReadClipboardSourceCode = @"using System;
using System.Windows.Forms;
class Program
{
    public static void Main()
    {
        if (string.IsNullOrEmpty(Clipboard.GetText()))
        {
            throw new Exception(""Clipboard empty!"");
        }
    }
}";
            Clipboard.SetText("clipboard test");
            var exePath = this.CreateExe("RestrictedProcessShouldNotBeAbleToReadClipboard.exe", ReadClipboardSourceCode);

            var process = new RestrictedProcessExecutor();
            var result = process.Execute(exePath, string.Empty, 1500, 32 * 1024 * 1024);

            Assert.NotNull(result);
            Assert.True(result.Type == ProcessExecutionResultType.RunTimeError, "No exception is thrown!");
        }

        [StaFact]
        public void RestrictedProcessShouldNotBeAbleToWriteToClipboard()
        {
            const string WriteToClipboardSourceCode = @"using System;
using System.Windows.Forms;
class Program
{
    public static void Main()
    {
        Clipboard.SetText(""i did it"");
    }
}";
            var exePath = this.CreateExe("RestrictedProcessShouldNotBeAbleToWriteToClipboard.exe", WriteToClipboardSourceCode);

            var process = new RestrictedProcessExecutor();
            var result = process.Execute(exePath, string.Empty, 1500, 32 * 1024 * 1024);

            Assert.NotNull(result);
            Assert.True(result.Type == ProcessExecutionResultType.RunTimeError, "No exception is thrown!");
            Assert.NotEqual("i did it", Clipboard.GetText());
        }

        [Fact]
        public void RestrictedProcessShouldNotBeAbleToStartProcess()
        {
            // Starting cmd.exe directly (without the shell): on modern Windows shell-brokered
            // executables like notepad.exe are launched by a system service outside the job object,
            // which the sandbox cannot (and does not need to) restrict.
            const string StartCmdProcessSourceCode = @"using System;
using System.Diagnostics;
class Program
{
    public static void Main()
    {
        var startInfo = new ProcessStartInfo(string.Format(""{0}\\cmd.exe"", Environment.SystemDirectory))
        {
            UseShellExecute = false,
        };
        Process.Start(startInfo);
    }
}";
            var exePath = this.CreateExe("RestrictedProcessShouldNotBeAbleToStartProcess.exe", StartCmdProcessSourceCode);

            var process = new RestrictedProcessExecutor();
            var result = process.Execute(exePath, string.Empty, 1500, 32 * 1024 * 1024);

            Assert.NotNull(result);
            Assert.True(result.Type == ProcessExecutionResultType.RunTimeError, "No exception is thrown!");
        }

        [Fact]
        public void RestrictedProcessShouldRunWithRestrictedToken()
        {
            const string PrintIsTokenRestrictedSourceCode = @"using System;
using System.Runtime.InteropServices;
class Program
{
    [DllImport(""kernel32.dll"")]
    static extern IntPtr GetCurrentProcess();

    [DllImport(""advapi32.dll"", SetLastError = true)]
    static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport(""advapi32.dll"", SetLastError = true)]
    static extern bool IsTokenRestricted(IntPtr tokenHandle);

    public static void Main()
    {
        IntPtr token;
        if (!OpenProcessToken(GetCurrentProcess(), 0x0008 /* TOKEN_QUERY */, out token))
        {
            throw new Exception(""OpenProcessToken failed!"");
        }
        Console.WriteLine(IsTokenRestricted(token));
    }
}";
            var exePath = this.CreateExe("RestrictedProcessShouldRunWithRestrictedToken.exe", PrintIsTokenRestrictedSourceCode);

            var process = new RestrictedProcessExecutor();
            var result = process.Execute(exePath, string.Empty, 1500, 32 * 1024 * 1024);

            Assert.NotNull(result);
            Assert.Equal(ProcessExecutionResultType.Success, result.Type);
            Assert.Equal("True", result.ReceivedOutput.Trim());
        }

        [Fact]
        public void RestrictedProcessShouldHaveNoPrivileges()
        {
            const string PrintPrivilegeCountSourceCode = @"using System;
using System.Runtime.InteropServices;
class Program
{
    [DllImport(""kernel32.dll"")]
    static extern IntPtr GetCurrentProcess();

    [DllImport(""advapi32.dll"", SetLastError = true)]
    static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport(""advapi32.dll"", SetLastError = true)]
    static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass, IntPtr tokenInformation, int tokenInformationLength, out int returnLength);

    public static void Main()
    {
        IntPtr token;
        if (!OpenProcessToken(GetCurrentProcess(), 0x0008 /* TOKEN_QUERY */, out token))
        {
            throw new Exception(""OpenProcessToken failed!"");
        }
        int length;
        GetTokenInformation(token, 3 /* TokenPrivileges */, IntPtr.Zero, 0, out length);
        IntPtr buffer = Marshal.AllocHGlobal(length);
        if (!GetTokenInformation(token, 3 /* TokenPrivileges */, buffer, length, out length))
        {
            throw new Exception(""GetTokenInformation failed!"");
        }
        Console.WriteLine(Marshal.ReadInt32(buffer)); // TOKEN_PRIVILEGES.PrivilegeCount
    }
}";
            var exePath = this.CreateExe("RestrictedProcessShouldHaveNoPrivileges.exe", PrintPrivilegeCountSourceCode);

            var process = new RestrictedProcessExecutor();
            var result = process.Execute(exePath, string.Empty, 1500, 32 * 1024 * 1024);

            Assert.NotNull(result);
            Assert.Equal(ProcessExecutionResultType.Success, result.Type);

            // DISABLE_MAX_PRIVILEGE keeps only SeChangeNotifyPrivilege
            var privilegeCount = int.Parse(result.ReceivedOutput.Trim());
            Assert.True(privilegeCount <= 1, $"The token holds {privilegeCount} privileges!");
        }

        [Fact]
        public void RestrictedProcessShouldRunAtLowIntegrityLevel()
        {
            const string PrintIntegrityLevelSourceCode = @"using System;
using System.Runtime.InteropServices;
class Program
{
    [DllImport(""kernel32.dll"")]
    static extern IntPtr GetCurrentProcess();

    [DllImport(""advapi32.dll"", SetLastError = true)]
    static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport(""advapi32.dll"", SetLastError = true)]
    static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass, IntPtr tokenInformation, int tokenInformationLength, out int returnLength);

    [DllImport(""advapi32.dll"")]
    static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

    [DllImport(""advapi32.dll"")]
    static extern IntPtr GetSidSubAuthority(IntPtr sid, int index);

    public static void Main()
    {
        IntPtr token;
        if (!OpenProcessToken(GetCurrentProcess(), 0x0008 /* TOKEN_QUERY */, out token))
        {
            throw new Exception(""OpenProcessToken failed!"");
        }
        int length;
        GetTokenInformation(token, 25 /* TokenIntegrityLevel */, IntPtr.Zero, 0, out length);
        IntPtr buffer = Marshal.AllocHGlobal(length);
        if (!GetTokenInformation(token, 25 /* TokenIntegrityLevel */, buffer, length, out length))
        {
            throw new Exception(""GetTokenInformation failed!"");
        }
        IntPtr sid = Marshal.ReadIntPtr(buffer); // TOKEN_MANDATORY_LABEL.Label.Sid
        int subAuthorityCount = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
        Console.WriteLine(Marshal.ReadInt32(GetSidSubAuthority(sid, subAuthorityCount - 1)));
    }
}";
            var exePath = this.CreateExe("RestrictedProcessShouldRunAtLowIntegrityLevel.exe", PrintIntegrityLevelSourceCode);

            var process = new RestrictedProcessExecutor();
            var result = process.Execute(exePath, string.Empty, 1500, 32 * 1024 * 1024);

            Assert.NotNull(result);
            Assert.Equal(ProcessExecutionResultType.Success, result.Type);
            Assert.Equal("4096", result.ReceivedOutput.Trim()); // S-1-16-4096 = Low integrity level
        }

        [Fact]
        public void RestrictedProcessShouldNotInheritUnrelatedHandles()
        {
            const string SetEventSourceCode = @"using System;
using System.Runtime.InteropServices;
class Program
{
    [DllImport(""kernel32.dll"", SetLastError = true)]
    static extern bool SetEvent(IntPtr eventHandle);

    public static void Main(string[] args)
    {
        Console.WriteLine(SetEvent((IntPtr)long.Parse(args[0])));
    }
}";
            var exePath = this.CreateExe("RestrictedProcessShouldNotInheritUnrelatedHandles.exe", SetEventSourceCode);

            // An inheritable event handle simulates any sensitive handle the host holds open.
            // Inherited handles keep their numeric value, so the child receives it as an argument.
            var securityAttributes = new NativeSecurityAttributes
            {
                Length = Marshal.SizeOf<NativeSecurityAttributes>(),
                SecurityDescriptor = IntPtr.Zero,
                InheritHandle = 1,
            };
            var eventHandle = NativeMethods.CreateEvent(ref securityAttributes, true, false, null);
            Assert.NotEqual(IntPtr.Zero, eventHandle);

            try
            {
                var arguments = new[] { eventHandle.ToInt64().ToString() };

                // With the handle whitelist the event handle is not inherited, so the child cannot
                // signal it: the strict-handle-checks mitigation turns the bad reference into a crash,
                // but either way the event stays unsignaled.
                var result = new RestrictedProcessExecutor().Execute(exePath, string.Empty, 1500, 32 * 1024 * 1024, arguments);
                Assert.NotEqual("True", result.ReceivedOutput.Trim());
                Assert.NotEqual(WaitObject0, NativeMethods.WaitForSingleObject(eventHandle, 0));

                // Control run: without the handle whitelist the leaked handle is inherited and usable
                var permissiveOptions = new RestrictedProcessOptions { RestrictInheritedHandles = false };
                var permissiveResult = new RestrictedProcessExecutor(permissiveOptions).Execute(exePath, string.Empty, 1500, 32 * 1024 * 1024, arguments);
                Assert.Equal("True", permissiveResult.ReceivedOutput.Trim());
                Assert.Equal(WaitObject0, NativeMethods.WaitForSingleObject(eventHandle, 0));
            }
            finally
            {
                NativeMethods.CloseHandle(eventHandle);
            }
        }

        [Fact]
        public void RestrictedProcessShouldHaveChildProcessCreationBlockedByPolicy()
        {
            const string PrintChildProcessPolicySourceCode = @"using System;
using System.Runtime.InteropServices;
class Program
{
    [DllImport(""kernel32.dll"")]
    static extern IntPtr GetCurrentProcess();

    [DllImport(""kernel32.dll"", SetLastError = true)]
    static extern bool GetProcessMitigationPolicy(IntPtr process, int policy, out int buffer, IntPtr length);

    public static void Main()
    {
        int flags;
        if (!GetProcessMitigationPolicy(GetCurrentProcess(), 13 /* ProcessChildProcessPolicy */, out flags, (IntPtr)4))
        {
            throw new Exception(""GetProcessMitigationPolicy failed!"");
        }
        Console.WriteLine(flags & 1); // NoChildProcessCreation
    }
}";
            var exePath = this.CreateExe("RestrictedProcessShouldHaveChildProcessCreationBlockedByPolicy.exe", PrintChildProcessPolicySourceCode);

            var process = new RestrictedProcessExecutor();
            var result = process.Execute(exePath, string.Empty, 1500, 32 * 1024 * 1024);

            Assert.NotNull(result);
            Assert.Equal(ProcessExecutionResultType.Success, result.Type);
            Assert.Equal("1", result.ReceivedOutput.Trim());
        }

        [Fact]
        public void RestrictedProcessShouldApplyDefaultProcessMitigations()
        {
            const string PrintExtensionPointPolicySourceCode = @"using System;
using System.Runtime.InteropServices;
class Program
{
    [DllImport(""kernel32.dll"")]
    static extern IntPtr GetCurrentProcess();

    [DllImport(""kernel32.dll"", SetLastError = true)]
    static extern bool GetProcessMitigationPolicy(IntPtr process, int policy, out int buffer, IntPtr length);

    public static void Main()
    {
        int flags;
        if (!GetProcessMitigationPolicy(GetCurrentProcess(), 6 /* ProcessExtensionPointDisablePolicy */, out flags, (IntPtr)4))
        {
            throw new Exception(""GetProcessMitigationPolicy failed!"");
        }
        Console.WriteLine(flags & 1); // DisableExtensionPoints
    }
}";
            var exePath = this.CreateExe("RestrictedProcessShouldApplyDefaultProcessMitigations.exe", PrintExtensionPointPolicySourceCode);

            var process = new RestrictedProcessExecutor();
            var result = process.Execute(exePath, string.Empty, 1500, 32 * 1024 * 1024);

            Assert.NotNull(result);
            Assert.Equal(ProcessExecutionResultType.Success, result.Type);
            Assert.Equal("1", result.ReceivedOutput.Trim());
        }

        [Fact]
        public void RestrictedProcessShouldDieOnWin32kSystemCallWhenLockedDown()
        {
            const string ShowMessageBoxSourceCode = @"using System;
using System.Runtime.InteropServices;
class Program
{
    [DllImport(""user32.dll"", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(IntPtr window, string text, string caption, uint type);

    public static void Main()
    {
        MessageBoxW(IntPtr.Zero, ""sandbox"", ""sandbox"", 0);
        Console.WriteLine(""SHOWED"");
    }
}";
            var exePath = this.CreateExe("RestrictedProcessShouldDieOnWin32kSystemCallWhenLockedDown.exe", ShowMessageBoxSourceCode);

            var options = new RestrictedProcessOptions
            {
                Mitigations = ProcessMitigations.Default | ProcessMitigations.Win32kSystemCallDisable,
            };
            var process = new RestrictedProcessExecutor(options);
            var result = process.Execute(exePath, string.Empty, 1000, 32 * 1024 * 1024);

            Assert.NotNull(result);
            Assert.DoesNotContain("SHOWED", result.ReceivedOutput);
            Assert.NotEqual(0, result.ExitCode);

            // TimeLimit would mean the message box was actually shown and the process had to be killed
            Assert.NotEqual(ProcessExecutionResultType.TimeLimit, result.Type);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSecurityAttributes
        {
            public int Length;
            public IntPtr SecurityDescriptor;
            public int InheritHandle;
        }

        private static class NativeMethods
        {
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern IntPtr CreateEvent(ref NativeSecurityAttributes eventAttributes, bool manualReset, bool initialState, string name);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CloseHandle(IntPtr handle);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        }
    }
}
