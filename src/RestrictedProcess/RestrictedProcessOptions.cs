// <copyright file="RestrictedProcessOptions.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess
{
    /// <summary>
    /// Configures how strongly a <see cref="Process.RestrictedProcess"/> is sandboxed.
    /// The defaults enable all hardening that a plain console executable can tolerate;
    /// use <see cref="Legacy"/> to reproduce the behavior of versions prior to 3.0.0.
    /// </summary>
    public class RestrictedProcessOptions
    {
        /// <summary>
        /// Gets options reproducing the behavior of versions prior to 3.0.0 (no token hardening).
        /// </summary>
        public static RestrictedProcessOptions Legacy =>
            new RestrictedProcessOptions
            {
                TokenLevel = TokenLevel.Unrestricted,
                IntegrityLevel = IntegrityLevel.Low,
                DisallowChildProcesses = false,
                RestrictInheritedHandles = false,
                Mitigations = ProcessMitigations.None,
            };

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
    }
}
