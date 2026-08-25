namespace RestrictedProcess.Tests
{
    using System.Windows.Forms;

    using Xunit;

    public class RestrictedProcessSecurityTests : BaseExecutorsTestClass
    {
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
    }
}
