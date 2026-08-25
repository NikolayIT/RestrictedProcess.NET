namespace RestrictedProcess.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Security.AccessControl;
    using System.Security.Principal;

    using RestrictedProcess.JobObjects;
    using RestrictedProcess.Process;

    using Xunit;

    /// <summary>
    /// The layers underneath the executor, tested directly. These are the pieces where a mistake is silent:
    /// a marshalling error in a job structure or an arithmetic slip in a limit does not throw, it just
    /// quietly applies the wrong number.
    /// </summary>
    public class JobAndTokenUnitTests
    {
        private const long ThreeGigabytes = 3L * 1024 * 1024 * 1024;

        [Fact]
        public void AMemoryLimitAboveTwoGigabytesSurvivesIntact()
        {
            // The limits used to be int, so anything past 2 GB silently clamped and the job ended up with a
            // smaller backstop than asked for.
            var info = PrepareJobObject.GetExtendedLimitInformation(new RestrictedProcessOptions(), ThreeGigabytes);

            Assert.Equal((ulong)ThreeGigabytes, (ulong)info.JobMemoryLimit);
            Assert.True(
                (info.BasicLimitInformation.LimitFlags & (uint)LimitFlags.JOB_OBJECT_LIMIT_JOB_MEMORY) != 0,
                "The job-wide memory limit flag was not set.");
        }

        [Fact]
        public void NoMemoryBackstopIsRequestedWhenThereIsNoMemoryLimit()
        {
            var info = PrepareJobObject.GetExtendedLimitInformation(new RestrictedProcessOptions(), 0);

            Assert.Equal(UIntPtr.Zero, info.JobMemoryLimit);
            Assert.True(
                (info.BasicLimitInformation.LimitFlags & (uint)LimitFlags.JOB_OBJECT_LIMIT_JOB_MEMORY) == 0,
                "A memory limit flag was set without a memory limit.");
        }

        [Fact]
        public void TheHardJobLimitsNeverIncludeTheOnesThatWouldLoseTheMeasurement()
        {
            var info = PrepareJobObject.GetExtendedLimitInformation(new RestrictedProcessOptions(), ThreeGigabytes);
            var flags = (LimitFlags)info.BasicLimitInformation.LimitFlags;

            // Both of these terminate the job the moment they trip, which destroys the overage the executor
            // needs in order to say why the program was stopped.
            Assert.False(flags.HasFlag(LimitFlags.JOB_OBJECT_LIMIT_JOB_TIME));
            Assert.False(flags.HasFlag(LimitFlags.JOB_OBJECT_LIMIT_PROCESS_TIME));
            Assert.False(flags.HasFlag(LimitFlags.JOB_OBJECT_LIMIT_PROCESS_MEMORY));

            Assert.True(flags.HasFlag(LimitFlags.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE));
            Assert.True(flags.HasFlag(LimitFlags.JOB_OBJECT_LIMIT_ACTIVE_PROCESS));
        }

        [Fact]
        public void NotificationLimitsCarryEachThresholdTheyWereGiven()
        {
            var limits = PrepareJobObject.GetNotificationLimits(
                ThreeGigabytes, TimeSpan.FromSeconds(4), 5 * 1024 * 1024);

            Assert.True(limits.HasValue);
            Assert.Equal((ulong)ThreeGigabytes, limits!.Value.JobMemoryLimit);
            Assert.Equal(TimeSpan.FromSeconds(4).Ticks, limits.Value.PerJobUserTimeLimit);
            Assert.Equal(5UL * 1024 * 1024, limits.Value.IoWriteBytesLimit);

            var flags = (LimitFlags)limits.Value.LimitFlags;
            Assert.True(flags.HasFlag(LimitFlags.JOB_OBJECT_LIMIT_JOB_MEMORY));
            Assert.True(flags.HasFlag(LimitFlags.JOB_OBJECT_LIMIT_JOB_TIME));
            Assert.True(flags.HasFlag(LimitFlags.JOB_OBJECT_LIMIT_JOB_WRITE_BYTES));
        }

        [Fact]
        public void NoNotificationLimitsAreRequestedWhenThereAreNoThresholds()
        {
            Assert.Null(PrepareJobObject.GetNotificationLimits(null, null, null));
            Assert.Null(PrepareJobObject.GetNotificationLimits(0, TimeSpan.Zero, 0));
        }

        [Fact]
        public void TheKernelGivesBackTheNotificationLimitsItWasGiven()
        {
            // A round trip through the kernel is the only way to know the structure is laid out the way
            // Windows expects; a wrong offset would come back as a different number rather than an error.
            var limits = PrepareJobObject.GetNotificationLimits(ThreeGigabytes, TimeSpan.FromSeconds(7), null);
            Assert.True(limits.HasValue);

            using (var job = new JobObject())
            {
                Assert.True(job.TrySetNotificationLimits(limits!.Value), "The OS rejected the notification limits.");

                var readBack = job.GetNotificationLimits();

                Assert.Equal(limits.Value.JobMemoryLimit, readBack.JobMemoryLimit);
                Assert.Equal(limits.Value.PerJobUserTimeLimit, readBack.PerJobUserTimeLimit);
                Assert.Equal(limits.Value.LimitFlags, readBack.LimitFlags);
            }
        }

        [Fact]
        public void AFreshJobHasNotAccountedAnythingYet()
        {
            using (var job = new JobObject())
            {
                var accounting = job.GetAccountingInformation();

                Assert.Equal(0u, accounting.BasicInfo.ActiveProcesses);
                Assert.Equal(0L, accounting.BasicInfo.TotalUserTime);
                Assert.Equal(0UL, accounting.IoInfo.WriteTransferCount);
            }
        }

        [Fact]
        public void EveryRunSidIsDistinctAndWellFormed()
        {
            var sids = Enumerable.Range(0, 200).Select(_ => SidFactory.CreateUniqueRunSid()).ToList();

            Assert.Equal(sids.Count, sids.Select(x => x.Value).Distinct().Count());
            Assert.All(sids, sid => Assert.StartsWith("S-1-5-21-", sid.Value, StringComparison.Ordinal));
            Assert.All(sids, sid => Assert.True(sid.BinaryLength > 0));
        }

        [Fact]
        public void WellKnownSidsResolveToTheExpectedValues()
        {
            Assert.Equal("S-1-5-12", SidFactory.Restricted.Value);
            Assert.Equal("S-1-1-0", SidFactory.Everyone.Value);
            Assert.Equal("S-1-5-32-545", SidFactory.BuiltinUsers.Value);
            Assert.Equal("S-1-5-4", SidFactory.Interactive.Value);
            Assert.Equal("S-1-0-0", SidFactory.Null.Value);
            Assert.Equal("S-1-15-2-1", SidFactory.AllApplicationPackages.Value);
        }

        [Fact]
        public void ASandboxExceptionSaysWhichStepFailedAndWhy()
        {
            const int AccessDenied = 5;

            var exception = SandboxException.FromWin32Error(SandboxStep.CreateDesktop, AccessDenied, "rp_probe");

            Assert.Equal(SandboxStep.CreateDesktop, exception.Step);
            Assert.Equal(AccessDenied, exception.NativeErrorCode);
            Assert.Contains("CreateDesktop", exception.Message, StringComparison.Ordinal);
            Assert.Contains("rp_probe", exception.Message, StringComparison.Ordinal);
            Assert.NotNull(exception.InnerException);
        }

        [Fact]
        public void ASandboxExceptionWithoutAWin32ErrorStillNamesTheStep()
        {
            var exception = SandboxException.For(SandboxStep.CreateProcess, "something specific went wrong");

            Assert.Equal(SandboxStep.CreateProcess, exception.Step);
            Assert.Equal(0, exception.NativeErrorCode);
            Assert.Contains("something specific went wrong", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AWritableDirectoryThatDoesNotExistIsRefusedClearly()
        {
            var missing = Path.Combine(System.IO.Path.GetTempPath(), "rp_missing_" + Guid.NewGuid().ToString("N"));

            var exception = Assert.Throws<SandboxException>(
                () => new WritableDirectoryGrant(SidFactory.CreateUniqueRunSid(), new[] { missing }));

            Assert.Contains(missing, exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GrantingAndRevokingAWritableDirectoryLeavesItsAclUnchanged()
        {
            var directory = Path.Combine(System.IO.Path.GetTempPath(), "rp_acl_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);

            try
            {
                var before = DescribeAcl(directory);

                using (new WritableDirectoryGrant(SidFactory.CreateUniqueRunSid(), new[] { directory }))
                {
                    Assert.NotEqual(before, DescribeAcl(directory));
                }

                Assert.Equal(before, DescribeAcl(directory));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static List<string> DescribeAcl(string directory)
        {
            var security = new DirectoryInfo(directory).GetAccessControl();
            return security
                .GetAccessRules(true, true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .Select(rule => rule.IdentityReference.Value + ":" + rule.FileSystemRights + ":" + rule.AccessControlType)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();
        }
    }
}
