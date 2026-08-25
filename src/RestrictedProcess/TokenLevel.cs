// <copyright file="TokenLevel.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess
{
    /// <summary>
    /// Determines how much the primary token of the sandboxed process is locked down. The levels mirror
    /// the token levels used by the Chromium Windows sandbox, plus the write-restricted variant used by
    /// sandboxes that have to run uncooperative binaries.
    /// <para>
    /// Two access checks matter here. Every access is first evaluated against the token's enabled groups;
    /// a <em>deny-only</em> group can only ever contribute a denial. If the token also carries
    /// <em>restricting SIDs</em>, a second check is run against that list alone and both must succeed, so
    /// the restricting list is an upper bound on what the process can reach.
    /// </para>
    /// <para>
    /// Stricter is not automatically better: a token that cannot read the .NET runtime, the C runtime or
    /// the executable itself produces a process that never starts. The levels are ordered by how much
    /// they take away, and the practical ceiling for a given workload has to be found by testing it.
    /// </para>
    /// </summary>
    public enum TokenLevel
    {
        /// <summary>
        /// The process runs with an unmodified copy of the parent's token. Protection comes only from the
        /// integrity level and the job object.
        /// </summary>
        Unrestricted = 0,

        /// <summary>
        /// All privileges except SeChangeNotifyPrivilege are removed and every group except
        /// <c>BUILTIN\Users</c>, <c>Everyone</c> and <c>INTERACTIVE</c> becomes deny-only. No restricting
        /// SIDs are applied, so access checks still succeed wherever those three groups are granted.
        /// </summary>
        Limited = 1,

        /// <summary>
        /// Everything from <see cref="Limited"/> plus restricting SIDs (<c>BUILTIN\Users</c>,
        /// <c>Everyone</c>, <c>RESTRICTED</c>, the logon SID and the unique per-run SID), so every access
        /// check must also pass against that reduced list. Equivalent to the Chromium
        /// <c>USER_LIMITED</c> level and the default: it is the strictest level a stock .NET Framework or
        /// native console executable reliably starts under.
        /// <para>
        /// Note what this does <em>not</em> do: <c>Everyone</c> and <c>BUILTIN\Users</c> are in the
        /// restricting list, and a Low integrity level only blocks writes, so the process can still read
        /// most files the host user can read. Use <see cref="WriteRestricted"/> or
        /// <see cref="StrictlyRestricted"/> when reads must be contained too.
        /// </para>
        /// </summary>
        Restricted = 2,

        /// <summary>
        /// A write-restricted token: the restricting SIDs are evaluated for <em>write</em> access only, so
        /// the process reads normally (and therefore starts reliably) but can only write where the unique
        /// per-run SID has been granted access explicitly. The token is also a LUA token, so administrative
        /// groups are deny-only even in an elevated host.
        /// <para>
        /// This is the level to prefer when the workload needs a writable scratch directory: pair it with
        /// <see cref="RestrictedProcessOptions.WritableDirectories"/>.
        /// </para>
        /// </summary>
        WriteRestricted = 3,

        /// <summary>
        /// Every group in the token becomes deny-only and the restricting SIDs are reduced to
        /// <c>RESTRICTED</c>, the logon SID and the unique per-run SID. This is the first level that
        /// meaningfully contains reads. Expect managed runtimes to fail to start unless the runtime
        /// directory grants <c>RESTRICTED</c>.
        /// </summary>
        StrictlyRestricted = 4,

        /// <summary>
        /// The Chromium <c>USER_LOCKDOWN</c> token: every group including the user SID is deny-only, the
        /// only restricting SIDs are the NULL SID (<c>S-1-0-0</c>) and the unique per-run SID, and every
        /// privilege is removed including SeChangeNotifyPrivilege. Practically nothing that has a security
        /// descriptor can be opened.
        /// <para>
        /// Chromium can use this because its targets cooperate: they start under a more capable
        /// impersonation token and drop to the lockdown token themselves. An unmodified executable gets no
        /// such help, so this level is only usable for statically linked native binaries whose entire
        /// dependency set is already mapped. It is offered for completeness, not as a general setting.
        /// </para>
        /// </summary>
        Lockdown = 5,
    }
}
