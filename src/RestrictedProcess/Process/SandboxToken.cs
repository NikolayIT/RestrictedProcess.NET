// <copyright file="SandboxToken.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.Process
{
    using System;
    using System.Security.Principal;

    /// <summary>
    /// The restricted primary token together with the identities it actually presents.
    /// <para>
    /// The identities travel with the token on purpose. Anything that has to be made reachable by the
    /// sandboxed process - the throwaway desktop above all - needs an ACE for a SID the token really
    /// carries, and re-deriving those from <see cref="WindowsIdentity"/> is not the same thing: the logon
    /// SID in particular can come back empty there, which silently produces a desktop the process cannot
    /// attach to and a startup failure with no useful error.
    /// </para>
    /// </summary>
    internal sealed class SandboxToken : IDisposable
    {
        public SandboxToken(SafeTokenHandle handle, SecurityIdentifier userSid, SecurityIdentifier? logonSid)
        {
            this.Handle = handle;
            this.UserSid = userSid;
            this.LogonSid = logonSid;
        }

        public SafeTokenHandle Handle { get; }

        /// <summary>
        /// Gets the user the token belongs to. Deny-only at the lockdown level, so it cannot be relied on
        /// as the only identity in an ACE.
        /// </summary>
        public SecurityIdentifier UserSid { get; }

        /// <summary>
        /// Gets the logon session SID, which stays an enabled group at every token level and is therefore
        /// the identity the first access check can always match.
        /// </summary>
        public SecurityIdentifier? LogonSid { get; }

        public void Dispose()
        {
            this.Handle.Dispose();
        }
    }
}
