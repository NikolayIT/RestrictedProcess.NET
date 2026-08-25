// <copyright file="IntegrityLevel.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess
{
    /// <summary>
    /// The mandatory integrity level assigned to the sandboxed process.
    /// The numeric values match the RID of the corresponding mandatory label SID (S-1-16-x).
    /// </summary>
    public enum IntegrityLevel
    {
        /// <summary>
        /// Untrusted integrity level (S-1-16-0). Blocks almost all write access.
        /// Most programs (including .NET Framework executables) cannot even start at this level.
        /// </summary>
        Untrusted = 0x0000,

        /// <summary>
        /// Low integrity level (S-1-16-4096). Write access is denied to almost all
        /// files and registry keys while programs can still start and run normally.
        /// </summary>
        Low = 0x1000,

        /// <summary>
        /// Medium integrity level (S-1-16-8192) - the default level of a standard user process.
        /// Provides no integrity-based protection.
        /// </summary>
        Medium = 0x2000,
    }
}
