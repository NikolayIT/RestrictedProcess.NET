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
            };

        /// <summary>
        /// Gets or sets how much the primary token of the sandboxed process is locked down.
        /// </summary>
        public TokenLevel TokenLevel { get; set; } = TokenLevel.Restricted;

        /// <summary>
        /// Gets or sets the mandatory integrity level of the sandboxed process.
        /// </summary>
        public IntegrityLevel IntegrityLevel { get; set; } = IntegrityLevel.Low;
    }
}
