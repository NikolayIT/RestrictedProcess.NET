// <copyright file="TokenLevel.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess
{
    /// <summary>
    /// Determines how much the primary token of the sandboxed process is locked down.
    /// The levels mirror the token levels used by the Chromium Windows sandbox.
    /// </summary>
    public enum TokenLevel
    {
        /// <summary>
        /// The process runs with an unmodified copy of the parent's token.
        /// Protection comes only from the integrity level and the job object.
        /// </summary>
        Unrestricted = 0,

        /// <summary>
        /// All privileges except SeChangeNotifyPrivilege are removed and the
        /// BUILTIN\Administrators group is converted to a deny-only group.
        /// </summary>
        Limited = 1,

        /// <summary>
        /// Everything from <see cref="Limited"/> plus restricting SIDs
        /// (Everyone, BUILTIN\Users, RESTRICTED and the logon SID), so every access check
        /// must also pass against this reduced list of SIDs.
        /// </summary>
        Restricted = 2,
    }
}
