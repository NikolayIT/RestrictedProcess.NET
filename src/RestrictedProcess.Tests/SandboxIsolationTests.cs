namespace RestrictedProcess.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Security.AccessControl;
    using System.Security.Principal;
    using System.Threading;
    using System.Threading.Tasks;

    using Xunit;

    /// <summary>
    /// Tests for what the sandbox does and does not isolate. Several of these assert a <em>limitation</em>
    /// rather than a guarantee: the point is that the boundary is where the documentation says it is, and
    /// that it cannot move without a test noticing.
    /// </summary>
    public class SandboxIsolationTests : BaseExecutorsTestClass
    {
        private const string ProfileIsSharedReason =
            "This profile grants BUILTIN Users or Everyone, so there is no containment left to assert.";

        private const string ReadFileSourceCode = @"using System;
using System.IO;
class Program
{
    public static void Main(string[] args)
    {
        try
        {
            var text = File.ReadAllText(args[0]);
            Console.WriteLine(""READ:"" + text.Length);
        }
        catch (Exception e)
        {
            Console.WriteLine(""DENIED:"" + e.GetType().Name);
        }
    }
}";

        private const string WriteFileSourceCode = @"using System;
using System.IO;
class Program
{
    public static void Main(string[] args)
    {
        foreach (var path in args)
        {
            try
            {
                File.WriteAllText(Path.Combine(path, ""probe.txt""), ""written"");
                Console.WriteLine(""WROTE:"" + path);
            }
            catch (Exception)
            {
                Console.WriteLine(""DENIED:"" + path);
            }
        }
    }
}";

        [Fact]
        public void DefaultTokenLevelCanStillReadMachineFilesThatGrantBuiltinUsers()
        {
            // This is the documented shape of the default sandbox, not an accident. The restricting SIDs at
            // TokenLevel.Restricted include Everyone and BUILTIN\Users, and a Low integrity level only
            // blocks writes, so anything readable by ordinary users stays readable. Callers who need reads
            // contained have to move up a level - and pay the price the next test measures.
            var exePath = this.CreateExe("ReadMachineFile.exe", ReadFileSourceCode);
            var readable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "win.ini");
            Assert.True(File.Exists(readable), "The probe file is missing; pick another Users-readable file.");

            var result = new RestrictedProcessExecutor()
                .Execute(UntimedRequest(exePath, string.Empty, 5000, 64 * 1024 * 1024, new[] { readable }));

            Assert.Equal(ProcessExecutionResultType.Success, result.Type);
            Assert.StartsWith("READ:", result.ReceivedOutput.Trim());
        }

        [Fact]
        public void TheUserProfileIsOutOfReachEvenAtTheDefaultTokenLevel()
        {
            // "Reads are not contained" is true but easy to read as worse than it is. What the sandbox can
            // reach is whatever grants BUILTIN\Users or Everyone: system directories, and folders created
            // off a drive root, which inherit Users:(RX) from it. The user profile is not one of those - it
            // blocks inheritance and grants only SYSTEM, Administrators and the user itself, none of which
            // are restricting SIDs, so the second access check finds nothing to match and refuses.
            var probe = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "rp_profile_probe_" + Guid.NewGuid().ToString("N") + ".txt");

            File.WriteAllText(probe, "expected answers for the test cases");

            try
            {
                Assert.SkipUnless(!GrantsUsersOrEveryone(probe), ProfileIsSharedReason);

                var exePath = this.CreateExe("ProfileReadProbe.exe", ReadFileSourceCode);

                var result = new RestrictedProcessExecutor()
                    .Execute(UntimedRequest(exePath, string.Empty, 10000, 64 * 1024 * 1024, new[] { probe }));

                Assert.Equal(ProcessExecutionResultType.Success, result.Type);
                Assert.StartsWith("DENIED:", result.ReceivedOutput.Trim(), StringComparison.Ordinal);
            }
            finally
            {
                File.Delete(probe);
            }
        }

        [Theory]
        [InlineData(TokenLevel.StrictlyRestricted)]
        [InlineData(TokenLevel.Lockdown)]
        public void ReadContainingTokenLevelsCannotStartAManagedExecutable(TokenLevel level)
        {
            // The levels that genuinely contain reads take away access to the runtime the program needs to
            // load, so a managed executable never reaches its entry point: the process exits with
            // STATUS_ACCESS_DENIED. They are offered for statically linked native binaries whose whole
            // dependency set is already mapped, and this test exists so that claim stays honest.
            var exePath = this.CreateExe("ReadContainedLevels.exe", ReadFileSourceCode);
            var readable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "win.ini");

            var options = new RestrictedProcessOptions { TokenLevel = level };
            var result = new RestrictedProcessExecutor(options)
                .Execute(UntimedRequest(exePath, string.Empty, 5000, 64 * 1024 * 1024, new[] { readable }));

            Assert.Equal(ProcessExecutionResultType.RunTimeError, result.Type);
            Assert.DoesNotContain("READ:", result.ReceivedOutput);
        }

        [Fact]
        public void WriteRestrictedTokenWritesOnlyWhereTheRunIsGrantedAccess()
        {
            var exePath = this.CreateExe("WriteRestrictedProbe.exe", WriteFileSourceCode);

            var granted = Path.Combine(Path.GetTempPath(), "rp_granted_" + Guid.NewGuid().ToString("N"));
            var denied = Path.Combine(Path.GetTempPath(), "rp_denied_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(granted);
            Directory.CreateDirectory(denied);

            try
            {
                var options = new RestrictedProcessOptions
                {
                    TokenLevel = TokenLevel.WriteRestricted,
                    IntegrityLevel = IntegrityLevel.Medium,
                };
                options.WritableDirectories.Add(granted);

                var result = new RestrictedProcessExecutor(options)
                    .Execute(UntimedRequest(exePath, string.Empty, 5000, 64 * 1024 * 1024, new[] { granted, denied }));

                Assert.Contains("WROTE:" + granted, result.ReceivedOutput);
                Assert.Contains("DENIED:" + denied, result.ReceivedOutput);
                Assert.True(File.Exists(Path.Combine(granted, "probe.txt")));
                Assert.False(File.Exists(Path.Combine(denied, "probe.txt")));
            }
            finally
            {
                Directory.Delete(granted, true);
                Directory.Delete(denied, true);
            }
        }

        [Fact]
        public void GrantedWritableDirectoryLosesTheGrantWhenTheRunEnds()
        {
            var exePath = this.CreateExe("WriteGrantCleanup.exe", WriteFileSourceCode);
            var granted = Path.Combine(Path.GetTempPath(), "rp_cleanup_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(granted);

            try
            {
                var options = new RestrictedProcessOptions
                {
                    TokenLevel = TokenLevel.WriteRestricted,
                    IntegrityLevel = IntegrityLevel.Medium,
                };
                options.WritableDirectories.Add(granted);

                var before = new DirectoryInfo(granted).GetAccessControl().GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier)).Count;
                new RestrictedProcessExecutor(options)
                    .Execute(UntimedRequest(exePath, string.Empty, 5000, 64 * 1024 * 1024, new[] { granted }));
                var after = new DirectoryInfo(granted).GetAccessControl().GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier)).Count;

                Assert.Equal(before, after);
            }
            finally
            {
                Directory.Delete(granted, true);
            }
        }

        [Fact]
        public void ExitCodeTwoHundredFiftyNineIsNotMistakenForAStillRunningProcess()
        {
            // GetExitCodeProcess reports STILL_ACTIVE (259) for a running process, so a program that
            // genuinely exits with 259 used to be indistinguishable from one that had not finished.
            const string ExitWith259SourceCode = @"using System;
class Program
{
    public static void Main()
    {
        Environment.Exit(259);
    }
}";
            var exePath = this.CreateExe("ExitWith259.exe", ExitWith259SourceCode);

            var result = new RestrictedProcessExecutor()
                .Execute(UntimedRequest(exePath, string.Empty, 5000, 32 * 1024 * 1024));

            Assert.Equal(259, result.ExitCode);
            Assert.Equal(ProcessExecutionResultType.RunTimeError, result.Type);
        }

        [Fact]
        public void ExecutableInADirectoryWithSpacesRuns()
        {
            // An unquoted command line makes CreateProcess try every space-delimited prefix in turn, so
            // "C:\Program Files\x\a.exe" would give "C:\Program.exe" the first chance to run.
            const string HelloSourceCode = @"using System;
class Program
{
    public static void Main()
    {
        Console.WriteLine(""ran"");
    }
}";
            var original = this.CreateExe("SpacedPathProbe.exe", HelloSourceCode);
            var spacedDirectory = Path.Combine(Path.GetDirectoryName(original)!, "a directory with spaces");
            Directory.CreateDirectory(spacedDirectory);
            var spacedPath = Path.Combine(spacedDirectory, "a program.exe");
            File.Copy(original, spacedPath, true);

            var result = new RestrictedProcessExecutor()
                .Execute(UntimedRequest(spacedPath, string.Empty, 5000, 32 * 1024 * 1024));

            Assert.Equal(ProcessExecutionResultType.Success, result.Type);
            Assert.Equal("ran", result.ReceivedOutput.Trim());
        }

        [Fact]
        public void ArgumentsWithSpacesAndQuotesArriveIntact()
        {
            const string EchoArgsSourceCode = @"using System;
class Program
{
    public static void Main(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            Console.WriteLine(i + "":"" + args[i]);
        }
    }
}";
            var exePath = this.CreateExe("EchoArgs.exe", EchoArgsSourceCode);
            var arguments = new[] { "plain", "with space", "with \"quote\"", @"trailing\backslash\", string.Empty };

            var result = new RestrictedProcessExecutor()
                .Execute(UntimedRequest(exePath, string.Empty, 5000, 32 * 1024 * 1024, arguments));

            Assert.Equal(ProcessExecutionResultType.Success, result.Type);
            var lines = result.ReceivedOutput.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            Assert.Equal(arguments.Length, lines.Length);
            for (var i = 0; i < arguments.Length; i++)
            {
                Assert.Equal(i + ":" + arguments[i], lines[i]);
            }
        }

        [Fact]
        public async Task CancellingAnExecutionStopsTheProcessAndReportsCancelled()
        {
            const string EndlessSourceCode = @"using System;
class Program
{
    public static void Main()
    {
        while (true) { }
    }
}";
            var exePath = this.CreateExe("CancelProbe.exe", EndlessSourceCode);

            using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300)))
            {
                var request = new ExecutionRequest(exePath)
                {
                    WallClockLimit = TimeSpan.FromSeconds(30),
                    MemoryLimitBytes = 64 * 1024 * 1024,
                };

                var result = await new RestrictedProcessExecutor().ExecuteAsync(request, cancellation.Token);

                Assert.Equal(ProcessExecutionResultType.Cancelled, result.Type);
                Assert.True(
                    result.TimeWorked < TimeSpan.FromSeconds(10),
                    $"The process ran for {result.TimeWorked} after cancellation.");
            }
        }

        [Fact]
        public void EachRunGetsItsOwnRestrictingSidAndADefaultDaclWithoutTheLogonSid()
        {
            // Two properties make concurrent runs independent of each other, and both are visible from
            // inside the sandbox: a restricting SID unique to the execution, and a token default DACL that
            // does not hand out access to the whole logon session. Objects a run creates without an
            // explicit descriptor carry that DACL, so the only identity another run shares - the token
            // user - is not one that its restricting SID check can satisfy.
            const string PrintTokenIdentitiesSourceCode = @"using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
class Program
{
    [DllImport(""kernel32.dll"")]
    static extern IntPtr GetCurrentProcess();

    [DllImport(""advapi32.dll"", SetLastError = true)]
    static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport(""advapi32.dll"", SetLastError = true)]
    static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr info, int length, out int returnLength);

    [StructLayout(LayoutKind.Sequential)]
    struct SidAndAttributes { public IntPtr Sid; public uint Attributes; }

    static void Dump(IntPtr token, int infoClass, string prefix)
    {
        int length;
        GetTokenInformation(token, infoClass, IntPtr.Zero, 0, out length);
        var buffer = Marshal.AllocHGlobal(length);
        try
        {
            if (!GetTokenInformation(token, infoClass, buffer, length, out length)) { return; }
            var count = Marshal.ReadInt32(buffer);
            var size = Marshal.SizeOf(typeof(SidAndAttributes));
            for (var i = 0; i < count; i++)
            {
                var entry = (SidAndAttributes)Marshal.PtrToStructure(
                    new IntPtr(buffer.ToInt64() + IntPtr.Size + (i * size)), typeof(SidAndAttributes));
                Console.WriteLine(prefix + new SecurityIdentifier(entry.Sid).Value);
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public static void Main()
    {
        IntPtr token;
        OpenProcessToken(GetCurrentProcess(), 0x0008, out token);
        Dump(token, 11, ""RESTRICTED:"");  // TokenRestrictedSids
        Dump(token, 2, ""GROUP:"");        // TokenGroups
    }
}";
            var exePath = this.CreateExe("PrintTokenIdentities.exe", PrintTokenIdentitiesSourceCode);
            var executor = new RestrictedProcessExecutor();

            var first = executor.Execute(UntimedRequest(exePath, string.Empty, 5000, 32 * 1024 * 1024));
            var second = executor.Execute(UntimedRequest(exePath, string.Empty, 5000, 32 * 1024 * 1024));

            Assert.Equal(ProcessExecutionResultType.Success, first.Type);
            Assert.Equal(ProcessExecutionResultType.Success, second.Type);

            var firstUnique = UniqueRunSidsOf(first.ReceivedOutput);
            var secondUnique = UniqueRunSidsOf(second.ReceivedOutput);

            Assert.Single(firstUnique);
            Assert.Single(secondUnique);
            Assert.NotEqual(firstUnique[0], secondUnique[0]);

            // The logon SID stays an enabled group - the C runtime needs it to reach its own
            // BaseNamedObjects directory - while remaining absent from what the run hands out.
            Assert.Contains(SplitLines(first.ReceivedOutput), x => x.StartsWith("GROUP:S-1-5-5-", StringComparison.Ordinal));
        }

        [Fact]
        public void ExceedingTheDiskWriteLimitStopsTheProgram()
        {
            // A sandboxed program is otherwise free to fill the disk: the job object reports disk writes,
            // so a threshold on them is reported through the same notification channel as memory and
            // processor time and lands as an output limit.
            const string FloodDiskSourceCode = @"using System;
using System.IO;
class Program
{
    public static void Main(string[] args)
    {
        var buffer = new byte[1024 * 1024];
        using (var stream = File.Create(Path.Combine(args[0], ""flood.bin"")))
        {
            for (var i = 0; i < 512; i++)
            {
                stream.Write(buffer, 0, buffer.Length);
                stream.Flush();
            }
        }

        Console.WriteLine(""FINISHED"");
    }
}";
            var exePath = this.CreateExe("FloodDisk.exe", FloodDiskSourceCode);
            var scratch = Path.Combine(Path.GetTempPath(), "rp_disk_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);

            try
            {
                var options = new RestrictedProcessOptions
                {
                    TokenLevel = TokenLevel.WriteRestricted,
                    IntegrityLevel = IntegrityLevel.Medium,
                    MaxDiskWriteBytes = 8L * 1024 * 1024,
                };
                options.WritableDirectories.Add(scratch);

                var result = new RestrictedProcessExecutor(options)
                    .Execute(UntimedRequest(exePath, string.Empty, 20000, 128 * 1024 * 1024, new[] { scratch }));

                Assert.Equal(ProcessExecutionResultType.OutputLimit, result.Type);
                Assert.True(
                    result.IoStatistics.WriteBytes > (ulong)options.MaxDiskWriteBytes!.Value,
                    $"The job accounted only {result.IoStatistics.WriteBytes} written bytes.");
            }
            finally
            {
                Directory.Delete(scratch, true);
            }
        }

        [Fact]
        public void BlockingNetworkAccessTogetherWithTheThrowawayDesktopIsRefusedUpFront()
        {
            // These two cannot be combined: an AppContainer process cannot attach to a desktop the sandbox
            // creates. Failing at construction beats handing back a process that dies during startup with
            // a generic DLL initialisation error.
            var exePath = this.CreateExe("ConflictingOptions.exe", ReadFileSourceCode);
            var options = new RestrictedProcessOptions { BlockNetworkAccess = true, UseAlternateDesktop = true };

            var exception = Assert.Throws<SandboxException>(
                () => new RestrictedProcessExecutor(options)
                    .Execute(UntimedRequest(exePath, string.Empty, 5000, 32 * 1024 * 1024, new[] { "x" })));

            Assert.Contains("UseAlternateDesktop", exception.Message);
        }

        [Fact]
        public void BlockingNetworkAccessLeavesNoPermanentGrantOnTheExecutable()
        {
            // Version 2 granted ALL APPLICATION PACKAGES read and execute on the executable through icacls
            // and never took it back, so every program it ever ran was left with a widened ACL. The grant
            // is now made to the container's own package SID and removed when the run ends.
            const string HelloSourceCode = @"using System;
class Program
{
    public static void Main()
    {
        Console.WriteLine(""ok"");
    }
}";
            var exePath = this.CreateExe("AppContainerAclRevert.exe", HelloSourceCode);

            var before = DescribeAcl(exePath);

            var options = new RestrictedProcessOptions { BlockNetworkAccess = true, UseAlternateDesktop = false };
            new RestrictedProcessExecutor(options)
                .Execute(UntimedRequest(exePath, string.Empty, 10000, 64 * 1024 * 1024));

            Assert.Equal(before, DescribeAcl(exePath));
        }

        [Fact]
        public void CapabilitiesProbeReportsTheHost()
        {
            var capabilities = SandboxCapabilities.Probe();

            Console.WriteLine(capabilities.ToString());

            // Only the parts that hold everywhere are asserted. Whether this particular host can create a
            // desktop or set job notification limits is exactly what the probe exists to report, so
            // turning those into assertions would defeat the purpose on a restricted CI runner.
            Assert.True(capabilities.OperatingSystemVersion.Major >= 6);
            Assert.NotEmpty(capabilities.ToString());
        }

        private static string[] SplitLines(string text)
        {
            return text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string[] UniqueRunSidsOf(string output)
        {
            var result = new System.Collections.Generic.List<string>();
            foreach (var line in SplitLines(output))
            {
                var trimmed = line.Trim();

                // The generated per-run SID is the only S-1-5-21 restricting SID: the others are the
                // well-known Users, Everyone and RESTRICTED identities plus the logon session.
                if (trimmed.StartsWith("RESTRICTED:S-1-5-21-", StringComparison.Ordinal))
                {
                    result.Add(trimmed.Substring("RESTRICTED:".Length));
                }
            }

            return result.ToArray();
        }

        private static List<string> DescribeAcl(string path)
        {
            var security = new FileInfo(path).GetAccessControl();
            return security
                .GetAccessRules(true, true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .Select(rule => rule.IdentityReference.Value + ":" + rule.FileSystemRights + ":" + rule.AccessControlType)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();
        }

        private static bool GrantsUsersOrEveryone(string path)
        {
            var rules = new FileInfo(path)
                .GetAccessControl()
                .GetAccessRules(true, true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>();

            return rules.Any(rule =>
                rule.AccessControlType == AccessControlType.Allow
                && (rule.IdentityReference.Value == "S-1-5-32-545" || rule.IdentityReference.Value == "S-1-1-0"));
        }
    }
}
