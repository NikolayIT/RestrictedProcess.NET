// <copyright file="ProcessExecutionResultType.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcessCore
{
    /// <summary>
    /// The process execution result type.
    /// </summary>
    public enum ProcessExecutionResultType
    {
        /// <summary>
        /// Success result.
        /// </summary>
        Success = 0,

        /// <summary>
        /// Time limit result.
        /// </summary>
        TimeLimit = 1,

        /// <summary>
        /// Memory limit result.
        /// </summary>
        MemoryLimit = 2,

        /// <summary>
        /// Run time error result.
        /// </summary>
        RunTimeError = 4,
    }
}
