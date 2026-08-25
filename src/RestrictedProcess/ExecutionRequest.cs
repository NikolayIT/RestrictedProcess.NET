// <copyright file="ExecutionRequest.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One sandboxed execution: what to run, what to feed it, and the limits it is judged against.
    /// <para>
    /// Processor time and wall-clock time are separate on purpose. A program that sleeps consumes no
    /// processor time, so a single limit either lets it idle indefinitely or fails programs that block on
    /// legitimate I/O. <see cref="CpuTimeLimit"/> is what the verdict is based on;
    /// <see cref="WallClockLimit"/> is the deadline at which the process is killed regardless.
    /// </para>
    /// </summary>
    public sealed class ExecutionRequest
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ExecutionRequest"/> class.
        /// </summary>
        /// <param name="fileName">The executable to run.</param>
        public ExecutionRequest(string fileName)
        {
            this.FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
        }

        /// <summary>
        /// Gets the executable to run.
        /// </summary>
        public string FileName { get; }

        /// <summary>
        /// Gets or sets the arguments. Each entry becomes exactly one argv entry in the child; quoting and
        /// escaping are handled for you.
        /// </summary>
        public IReadOnlyList<string>? Arguments { get; set; }

        /// <summary>
        /// Gets or sets the text written to the standard input of the program.
        /// </summary>
        public string Input { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the processor time limit the verdict is based on. Null means unlimited.
        /// </summary>
        public TimeSpan? CpuTimeLimit { get; set; }

        /// <summary>
        /// Gets or sets the wall-clock deadline after which the process is killed. Null derives it from
        /// <see cref="CpuTimeLimit"/> and
        /// <see cref="RestrictedProcessOptions.WallClockWaitMultiplier"/>.
        /// </summary>
        public TimeSpan? WallClockLimit { get; set; }

        /// <summary>
        /// Gets or sets the memory limit in bytes. Null means unlimited.
        /// </summary>
        public long? MemoryLimitBytes { get; set; }

        /// <summary>
        /// Gets or sets the working directory. Null uses
        /// <see cref="RestrictedProcessOptions.WorkingDirectory"/>, and failing that the directory the
        /// executable lives in.
        /// </summary>
        public string? WorkingDirectory { get; set; }
    }
}
