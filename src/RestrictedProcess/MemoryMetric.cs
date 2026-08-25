// <copyright file="MemoryMetric.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess
{
    /// <summary>
    /// Which memory figure a run is judged on. These measure genuinely different things, so mixing them
    /// (as taking the maximum of both does) produces a number that means nothing in particular.
    /// </summary>
    public enum MemoryMetric
    {
        /// <summary>
        /// The peak memory committed by every process in the job. Committed bytes are what the program
        /// asked the OS to back, so this is deterministic, reproducible between machines, and available
        /// after the process has exited. The right default for judging.
        /// </summary>
        PeakCommit = 0,

        /// <summary>
        /// The peak working set of the root process: how much of it was physically resident. This moves
        /// with system memory pressure and with what else is running, so two runs of the same program on
        /// different machines can disagree. It is also only observable while the process is alive.
        /// </summary>
        PeakWorkingSet = 1,

        /// <summary>
        /// The larger of the two. Matches what version 1 of this library reported; kept so an existing
        /// judge can reproduce historical verdicts, but it is not a metric with a clean meaning.
        /// </summary>
        Max = 2,
    }
}
