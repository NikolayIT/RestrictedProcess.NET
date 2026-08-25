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
    /// Configures how strongly a <see cref="Process.RestrictedProcess"/> is sandboxed.
    /// The defaults enable all hardening that a plain console executable can tolerate.
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
        /// Gets or sets a value indicating whether the process is prevented from creating
        /// child processes at the kernel level (PROC_THREAD_ATTRIBUTE_CHILD_PROCESS_POLICY),
        /// independently of the job object's active process limit.
        /// </summary>
        public bool DisallowChildProcesses { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether the process inherits only its three standard
        /// IO pipe handles instead of every inheritable handle of the parent process
        /// (PROC_THREAD_ATTRIBUTE_HANDLE_LIST).
        /// </summary>
        public bool RestrictInheritedHandles { get; set; } = true;

        /// <summary>
        /// Gets or sets the process creation mitigation policies applied to the process.
        /// </summary>
        public ProcessMitigations Mitigations { get; set; } = ProcessMitigations.Default;

        /// <summary>
        /// Gets or sets a value indicating whether the process runs on a throwaway desktop, so it
        /// cannot enumerate, read or send window messages to windows on the interactive desktop.
        /// </summary>
        public bool UseAlternateDesktop { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether the process is denied network access by running it
        /// inside an AppContainer with no network capabilities (the Windows Firewall then blocks its
        /// sockets). Off by default. Requires the Windows Firewall / Base Filtering Engine service to
        /// be running, and grants the "ALL APPLICATION PACKAGES" identity read and execute rights on
        /// the executable so the AppContainer process can load it.
        /// </summary>
        public bool BlockNetworkAccess { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of processes that may be simultaneously active in the job.
        /// </summary>
        public int ActiveProcessLimit { get; set; } = 1;

        /// <summary>
        /// Gets or sets the priority class the process runs at.
        /// </summary>
        public ProcessPriorityClass PriorityClass { get; set; } = ProcessPriorityClass.High;

        /// <summary>
        /// Gets or sets the processor affinity mask applied to every process in the job.
        /// Null leaves the affinity unrestricted.
        /// </summary>
        public UIntPtr? ProcessorAffinityMask { get; set; }

        /// <summary>
        /// Gets or sets a hard CPU rate cap for the job, as a percentage (1-100) of total CPU time.
        /// Null leaves the CPU rate unrestricted.
        /// </summary>
        public int? CpuRateLimitPercent { get; set; }

        /// <summary>
        /// Gets or sets the multiplier applied to the requested time and memory limits to derive
        /// the hard job object backstop enforced by the OS (the precise limits are enforced by the
        /// executor). Must be greater than or equal to 1.
        /// </summary>
        public double JobLimitsMultiplier { get; set; } = 2.0;

        /// <summary>
        /// Gets or sets the multiplier applied to the requested time limit to derive how long the
        /// executor waits (wall-clock) before killing the process. Must be greater than or equal to 1.
        /// </summary>
        public double WallClockWaitMultiplier { get; set; } = 1.5;

        /// <summary>
        /// Gets or sets the maximum number of characters read from the standard output before the
        /// process is stopped and the result is classified as an output limit. Zero means unlimited.
        /// </summary>
        public long MaxOutputSize { get; set; } = 64 * 1024 * 1024;

        /// <summary>
        /// Gets or sets the maximum number of characters read from the standard error before the
        /// process is stopped and the result is classified as an output limit. Zero means unlimited.
        /// </summary>
        public long MaxErrorSize { get; set; } = 16 * 1024 * 1024;

        /// <summary>
        /// Gets or sets a value indicating whether a non-zero process exit code is reported as a
        /// runtime error (unless the run already tripped a time, memory or output limit).
        /// </summary>
        public bool TreatNonZeroExitCodeAsRunTimeError { get; set; } = true;

        /// <summary>
        /// Gets or sets the encoding used for the process standard IO. Null uses the system's
        /// active ANSI code page, which is what console child processes write by default.
        /// </summary>
        public Encoding? Encoding { get; set; }

        /// <summary>
        /// Gets or sets the working directory of the process. Null uses the directory of the executable.
        /// </summary>
        public string? WorkingDirectory { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the process receives a minimal environment
        /// block (a handful of system variables) instead of inheriting the parent's environment,
        /// which may hold secrets.
        /// </summary>
        public bool ScrubEnvironment { get; set; } = true;

        /// <summary>
        /// Gets extra environment variables merged into the environment of the sandboxed process.
        /// When <see cref="ScrubEnvironment"/> is true they are added to the minimal block;
        /// otherwise they are added to (and override) the inherited environment.
        /// </summary>
        public IDictionary<string, string> AdditionalEnvironmentVariables { get; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
