// <copyright file="RestrictedProcessStartInfo.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.Process
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// What a <see cref="RestrictedProcess"/> should run, and the limits that have to be configured on the
    /// job object before the process is created.
    /// </summary>
    public sealed class RestrictedProcessStartInfo
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RestrictedProcessStartInfo"/> class.
        /// </summary>
        /// <param name="fileName">The executable to run.</param>
        public RestrictedProcessStartInfo(string fileName)
        {
            this.FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
        }

        /// <summary>
        /// Gets the executable to run.
        /// </summary>
        public string FileName { get; }

        /// <summary>
        /// Gets or sets the arguments passed to the executable. Each entry becomes exactly one argv entry;
        /// quoting and escaping are handled by the library.
        /// </summary>
        public IReadOnlyList<string>? Arguments { get; set; }

        /// <summary>
        /// Gets or sets the working directory of the process.
        /// </summary>
        public string? WorkingDirectory { get; set; }

        /// <summary>
        /// Gets or sets the committed memory limit in bytes. Used both as the soft notification threshold
        /// and, multiplied by the job limits multiplier, as the hard job backstop.
        /// </summary>
        public long? MemoryLimitBytes { get; set; }

        /// <summary>
        /// Gets or sets the processor time limit, applied as a soft job notification limit so a breach is
        /// reported the moment it happens without terminating the job.
        /// </summary>
        public TimeSpan? CpuTimeLimit { get; set; }

        /// <summary>
        /// Gets or sets the size of the standard IO pipe buffers.
        /// </summary>
        public int PipeBufferSize { get; set; } = 64 * 1024;

        /// <summary>
        /// Gets or sets the encoding of the standard IO streams. Null uses the system ANSI code page.
        /// </summary>
        public Encoding? Encoding { get; set; }
    }
}
