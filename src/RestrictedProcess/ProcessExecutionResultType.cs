// <copyright file="ProcessExecutionResultType.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess
{
    /// <summary>
    /// How a sandboxed run ended.
    /// </summary>
    public enum ProcessExecutionResultType
    {
        /// <summary>
        /// The program ran to completion within every limit.
        /// </summary>
        Success = 0,

        /// <summary>
        /// The program exceeded its processor time limit or its wall-clock deadline.
        /// </summary>
        TimeLimit = 1,

        /// <summary>
        /// The program exceeded its memory limit.
        /// </summary>
        MemoryLimit = 2,

        /// <summary>
        /// The program wrote to standard error, exited with a non-zero code, or crashed.
        /// </summary>
        RunTimeError = 3,

        /// <summary>
        /// The program produced more output than the configured cap allows.
        /// </summary>
        OutputLimit = 4,

        /// <summary>
        /// The caller cancelled the execution before it finished.
        /// </summary>
        Cancelled = 5,
    }
}
