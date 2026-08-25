// <copyright file="ProcessExecutionResult.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess
{
    using System;

    /// <summary>
    /// What a sandboxed run produced and what it cost.
    /// </summary>
    public class ProcessExecutionResult
    {
        /// <summary>
        /// Gets or sets what the program wrote to standard output, truncated at the configured cap.
        /// </summary>
        public string ReceivedOutput { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets what the program wrote to standard error, truncated at the configured cap.
        /// </summary>
        public string ErrorOutput { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether standard output hit the cap and was cut short.
        /// </summary>
        public bool OutputTruncated { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether standard error hit the cap and was cut short.
        /// </summary>
        public bool ErrorTruncated { get; set; }

        /// <summary>
        /// Gets or sets the exit code of the process.
        /// </summary>
        public int ExitCode { get; set; }

        /// <summary>
        /// Gets or sets how the run ended.
        /// </summary>
        public ProcessExecutionResultType Type { get; set; } = ProcessExecutionResultType.Success;

        /// <summary>
        /// Gets or sets the wall-clock time from process creation to exit.
        /// </summary>
        public TimeSpan TimeWorked { get; set; }

        /// <summary>
        /// Gets or sets the kernel-mode processor time used by every process in the job.
        /// </summary>
        public TimeSpan PrivilegedProcessorTime { get; set; }

        /// <summary>
        /// Gets or sets the user-mode processor time used by every process in the job.
        /// </summary>
        public TimeSpan UserProcessorTime { get; set; }

        /// <summary>
        /// Gets the total processor time used by every process in the job, including any that exited
        /// before the root process did.
        /// </summary>
        public TimeSpan TotalProcessorTime => this.PrivilegedProcessorTime + this.UserProcessorTime;

        /// <summary>
        /// Gets or sets the memory figure the run was judged on, selected by
        /// <see cref="RestrictedProcessOptions.MemoryMetric"/>.
        /// </summary>
        public long MemoryUsed { get; set; }

        /// <summary>
        /// Gets or sets the peak memory committed by every process in the job. Reproducible between
        /// machines and still available after the process has exited.
        /// </summary>
        public long PeakCommitBytes { get; set; }

        /// <summary>
        /// Gets or sets the peak working set of the root process: how much of it was physically resident.
        /// Depends on system memory pressure, so it is not reproducible between machines.
        /// </summary>
        public long PeakWorkingSetBytes { get; set; }

        /// <summary>
        /// Gets or sets the I/O every process in the job performed.
        /// </summary>
        public ProcessIoStatistics IoStatistics { get; set; }
    }
}
