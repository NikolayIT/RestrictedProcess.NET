// <copyright file="RestrictedProcessOptions.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text;

    /// <summary>
    /// Configures how strongly a <see cref="Process.RestrictedProcess"/> is sandboxed. The defaults enable
    /// every hardening measure a plain console executable, including a .NET Framework one, can tolerate.
    /// </summary>
    public class RestrictedProcessOptions
    {
        /// <summary>
        /// Gets or sets how much the primary token of the sandboxed process is locked down.
        /// </summary>
        public TokenLevel TokenLevel { get; set; } = TokenLevel.Restricted;

        /// <summary>
        /// Gets or sets the mandatory integrity level of the sandboxed process.
        /// </summary>
        public IntegrityLevel IntegrityLevel { get; set; } = IntegrityLevel.Low;

        /// <summary>
        /// Gets or sets a value indicating whether the token's default DACL is narrowed to the token user,
        /// the logon SID and the unique per-run SID. Every kernel object the sandboxed process creates
        /// without an explicit descriptor inherits this DACL, so narrowing it is what stops one sandboxed
        /// run from opening the objects of another.
        /// </summary>
        public bool LockdownTokenDefaultDacl { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether no-read-up and no-execute-up are added to the token's
        /// own mandatory label, so a lower integrity process cannot open it to duplicate or impersonate.
        /// Applied on a best-effort basis; it needs WRITE_OWNER on the token.
        /// </summary>
        public bool HardenTokenIntegrityPolicy { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether the process is prevented from creating child processes
        /// at the kernel level (PROC_THREAD_ATTRIBUTE_CHILD_PROCESS_POLICY), independently of the job
        /// object's active process limit. This also blocks shell-brokered launches from breaking away from
        /// the process tree, which is how a packaged-app alias such as notepad.exe would otherwise end up
        /// running outside the job.
        /// </summary>
        public bool DisallowChildProcesses { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether the process inherits only its three standard IO pipe
        /// handles instead of every inheritable handle of the parent (PROC_THREAD_ATTRIBUTE_HANDLE_LIST).
        /// </summary>
        public bool RestrictInheritedHandles { get; set; } = true;

        /// <summary>
        /// Gets or sets the first word of process creation mitigation policies applied to the process.
        /// </summary>
        public ProcessMitigations Mitigations { get; set; } = ProcessMitigations.Default;

        /// <summary>
        /// Gets or sets the second word of process creation mitigation policies (Windows 10 1703 and
        /// later). Off by default: several of these cost measurable performance, which matters when the
        /// sandbox is timing a program.
        /// </summary>
        public ProcessMitigations2 Mitigations2 { get; set; } = ProcessMitigations2.None;

        /// <summary>
        /// Gets or sets a value indicating whether the process runs on a throwaway desktop, so it cannot
        /// enumerate, read or send window messages to windows on the interactive desktop.
        /// </summary>
        public bool UseAlternateDesktop { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether an alternate window station is created alongside the
        /// desktop. A window station owns the clipboard and the atom table, so this closes the last shared
        /// USER surfaces - but creating a desktop on it requires switching the host process to that station
        /// and back, which is a process-wide change. Off by default for that reason.
        /// </summary>
        public bool UseAlternateWindowStation { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the process is denied network access by running it
        /// inside an AppContainer with no network capability, which makes the Windows Firewall drop its
        /// sockets. Requires the Windows Firewall / Base Filtering Engine service to be running.
        /// </summary>
        public bool BlockNetworkAccess { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the AppContainer is a Less Privileged AppContainer, in
        /// which the process is not granted anything through the ALL APPLICATION PACKAGES identity. Only
        /// meaningful together with <see cref="BlockNetworkAccess"/>.
        /// </summary>
        public bool UseLowPrivilegeAppContainer { get; set; }

        /// <summary>
        /// Gets or sets the name of the AppContainer profile used for network blocking. The profile is
        /// registered once and reused, because registering one writes to the registry and creates a
        /// directory under the user profile.
        /// </summary>
        public string AppContainerProfileName { get; set; } = "RestrictedProcess.NET.Sandbox";

        /// <summary>
        /// Gets the directories the sandboxed process is allowed to write to. The unique per-run SID is
        /// granted modify rights on each one before the process starts and the grant is removed afterwards.
        /// Only useful with <see cref="RestrictedProcess.TokenLevel.WriteRestricted"/>, where writes are
        /// exactly what the restricting SIDs gate.
        /// </summary>
        public IList<string> WritableDirectories { get; } = new List<string>();

        /// <summary>
        /// Gets or sets the maximum number of processes that may be simultaneously active in the job.
        /// </summary>
        public int ActiveProcessLimit { get; set; } = 1;

        /// <summary>
        /// Gets or sets the priority class the process runs at.
        /// </summary>
        public ProcessPriorityClass PriorityClass { get; set; } = ProcessPriorityClass.High;

        /// <summary>
        /// Gets or sets the processor affinity mask applied to every process in the job. Null leaves the
        /// affinity unrestricted.
        /// </summary>
        public UIntPtr? ProcessorAffinityMask { get; set; }

        /// <summary>
        /// Gets or sets a hard CPU rate cap for the job, as a percentage (1-100) of total CPU time across
        /// all cores. A single-threaded program is only throttled when the cap is below one core's share,
        /// which is 100 divided by the processor count.
        /// </summary>
        public int? CpuRateLimitPercent { get; set; }

        /// <summary>
        /// Gets or sets which memory figure <see cref="ProcessExecutionResult.MemoryUsed"/> reports and the
        /// memory limit is compared against.
        /// </summary>
        public MemoryMetric MemoryMetric { get; set; } = MemoryMetric.PeakCommit;

        /// <summary>
        /// Gets or sets the multiplier applied to the requested memory limit to derive the hard job object
        /// backstop enforced by the OS. The precise limit is enforced by the executor, which is why the
        /// backstop has to be looser: the program must be allowed to allocate past the limit for the
        /// overage to be measurable. Must be greater than or equal to 1.
        /// </summary>
        public double JobLimitsMultiplier { get; set; } = 2.0;

        /// <summary>
        /// Gets or sets the multiplier applied to the processor time limit to derive the wall-clock
        /// deadline, for callers that do not set a wall-clock limit explicitly. Must be at least 1.
        /// </summary>
        public double WallClockWaitMultiplier { get; set; } = 1.5;

        /// <summary>
        /// Gets or sets the maximum number of characters read from the standard output before the process
        /// is stopped and the result is classified as an output limit. Zero means unlimited.
        /// </summary>
        public long MaxOutputSize { get; set; } = 64 * 1024 * 1024;

        /// <summary>
        /// Gets or sets the maximum number of characters read from the standard error before the process is
        /// stopped and the result is classified as an output limit. Zero means unlimited.
        /// </summary>
        public long MaxErrorSize { get; set; } = 16 * 1024 * 1024;

        /// <summary>
        /// Gets or sets a job-wide limit on bytes written to disk, reported as
        /// <see cref="ProcessExecutionResultType.OutputLimit"/>. Null leaves disk writes unlimited.
        /// <para>
        /// Enforced twice over. It is registered as a job notification limit, which stops the program
        /// mid-write on systems that deliver that notification; and the job's accumulated write counter is
        /// compared against the limit after the run, so the verdict is still correct where the
        /// notification never arrives - which is the case on ordinary NTFS volumes, where the memory and
        /// processor time notifications fire reliably but the disk one does not.
        /// </para>
        /// </summary>
        public long? MaxDiskWriteBytes { get; set; }

        /// <summary>
        /// Gets or sets how long the executor waits for the output pipes to drain after the process has
        /// gone. Reaching end of file only requires the last write handle to be closed, which happens when
        /// the process dies, so this is a safety net rather than a normal code path.
        /// </summary>
        public TimeSpan OutputDrainTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Gets or sets a value indicating whether a non-zero process exit code is reported as a runtime
        /// error, unless the run already tripped a time, memory or output limit.
        /// </summary>
        public bool TreatNonZeroExitCodeAsRunTimeError { get; set; } = true;

        /// <summary>
        /// Gets or sets the encoding used for the process standard IO. Null uses the system's active ANSI
        /// code page, which is what console child processes write by default.
        /// </summary>
        public Encoding? Encoding { get; set; }

        /// <summary>
        /// Gets or sets the working directory of the process. Null uses the directory of the executable.
        /// </summary>
        public string? WorkingDirectory { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the process receives a minimal environment block instead
        /// of inheriting the parent's environment, which may hold secrets.
        /// </summary>
        public bool ScrubEnvironment { get; set; } = true;

        /// <summary>
        /// Gets extra environment variables merged into the environment of the sandboxed process.
        /// </summary>
        public IDictionary<string, string> AdditionalEnvironmentVariables { get; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
