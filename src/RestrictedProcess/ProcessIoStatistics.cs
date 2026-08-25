// <copyright file="ProcessIoStatistics.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess
{
    /// <summary>
    /// The I/O a sandboxed run performed, accumulated across every process in the job.
    /// </summary>
    public readonly struct ProcessIoStatistics
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ProcessIoStatistics"/> struct.
        /// </summary>
        /// <param name="readOperations">The number of read operations.</param>
        /// <param name="writeOperations">The number of write operations.</param>
        /// <param name="readBytes">The number of bytes read.</param>
        /// <param name="writeBytes">The number of bytes written.</param>
        public ProcessIoStatistics(ulong readOperations, ulong writeOperations, ulong readBytes, ulong writeBytes)
        {
            this.ReadOperations = readOperations;
            this.WriteOperations = writeOperations;
            this.ReadBytes = readBytes;
            this.WriteBytes = writeBytes;
        }

        /// <summary>
        /// Gets the number of read operations performed.
        /// </summary>
        public ulong ReadOperations { get; }

        /// <summary>
        /// Gets the number of write operations performed.
        /// </summary>
        public ulong WriteOperations { get; }

        /// <summary>
        /// Gets the number of bytes read.
        /// </summary>
        public ulong ReadBytes { get; }

        /// <summary>
        /// Gets the number of bytes written.
        /// </summary>
        public ulong WriteBytes { get; }
    }
}
