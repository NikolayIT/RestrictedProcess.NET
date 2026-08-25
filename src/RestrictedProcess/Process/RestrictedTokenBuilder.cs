// <copyright file="RestrictedTokenBuilder.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.Process
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.InteropServices;
    using System.Security.AccessControl;
    using System.Security.Principal;

    /// <summary>
    /// Builds the primary token the sandboxed process runs under.
    /// <para>
    /// The construction follows the Chromium sandbox: every group in the token is turned into a deny-only
    /// group except an explicit exception list, an explicit set of restricting SIDs bounds what the token
    /// can reach at all, privileges are dropped, the default DACL is narrowed so objects the process
    /// creates are not reachable by other processes of the same user, and the token's own mandatory label
    /// is hardened so a lower integrity process cannot open it for impersonation.
    /// </para>
    /// </summary>
    internal static class RestrictedTokenBuilder
    {
        private const int GenericAll = 0x10000000;

        private const byte SystemMandatoryLabelAceType = 0x11;

        private const int LabelSecurityInformation = 0x00000010;

        private const uint SystemMandatoryLabelNoWriteUp = 0x1;
        private const uint SystemMandatoryLabelNoReadUp = 0x2;
        private const uint SystemMandatoryLabelNoExecuteUp = 0x4;

        /// <summary>
        /// Creates the restricted primary token for a sandboxed run.
        /// </summary>
        /// <param name="options">The sandbox options selecting the token level and integrity level.</param>
        /// <param name="uniqueRunSid">The SID unique to this execution.</param>
        /// <returns>The token to pass to CreateProcessAsUser, with the identities it presents.</returns>
        public static SandboxToken Create(RestrictedProcessOptions options, SecurityIdentifier uniqueRunSid)
        {
            using (var processToken = OpenCurrentProcessToken())
            {
                var groups = ReadTokenGroups(processToken);
                var userSid = ReadTokenUser(processToken);
                var logonSid = FindLogonSid(groups);

                var plan = BuildPlan(options.TokenLevel, groups, userSid, logonSid, uniqueRunSid);

                var restrictedToken = CreateRestrictedToken(processToken, plan);
                try
                {
                    if (plan.RemoveEveryPrivilege)
                    {
                        RemoveAllPrivileges(restrictedToken);
                    }

                    if (options.LockdownTokenDefaultDacl)
                    {
                        SetDefaultDacl(restrictedToken, userSid, uniqueRunSid);
                    }

                    SetIntegrityLevel(restrictedToken, options.IntegrityLevel);

                    if (options.HardenTokenIntegrityPolicy)
                    {
                        // Best effort: this needs WRITE_OWNER on the token, which is not guaranteed. The
                        // integrity level itself is already applied above, so failing here only means the
                        // token stays openable from a lower integrity level.
                        TryHardenIntegrityPolicy(restrictedToken, options.IntegrityLevel);
                    }

                    return new SandboxToken(
                        restrictedToken,
                        new SecurityIdentifier(userSid, 0),
                        logonSid == null ? null : new SecurityIdentifier(logonSid, 0));
                }
                catch
                {
                    restrictedToken.Dispose();
                    throw;
                }
            }
        }

        private static TokenPlan BuildPlan(
            TokenLevel level,
            IReadOnlyList<TokenGroup> groups,
            byte[] userSid,
            byte[]? logonSid,
            SecurityIdentifier uniqueRunSid)
        {
            var plan = new TokenPlan();
            var unique = SidFactory.ToBinary(uniqueRunSid);

            switch (level)
            {
                case TokenLevel.Unrestricted:
                    break;

                case TokenLevel.Limited:
                    plan.Flags = CreateRestrictedTokenFlags.DISABLE_MAX_PRIVILEGE;
                    plan.DenyOnlySids.AddRange(AllGroupsExcept(groups, SidFactory.BuiltinUsers, SidFactory.Everyone, SidFactory.Interactive));
                    break;

                case TokenLevel.Restricted:
                    plan.Flags = CreateRestrictedTokenFlags.DISABLE_MAX_PRIVILEGE;
                    plan.DenyOnlySids.AddRange(AllGroupsExcept(groups, SidFactory.BuiltinUsers, SidFactory.Everyone, SidFactory.Interactive));
                    plan.RestrictingSids.Add(SidFactory.ToBinary(SidFactory.BuiltinUsers));
                    plan.RestrictingSids.Add(SidFactory.ToBinary(SidFactory.Everyone));
                    plan.RestrictingSids.Add(SidFactory.ToBinary(SidFactory.Restricted));
                    plan.RestrictingSids.Add(unique);
                    AddIfPresent(plan.RestrictingSids, logonSid);
                    break;

                case TokenLevel.WriteRestricted:
                    // The restricting SIDs of a write-restricted token gate write access only, so reads
                    // keep working and the binary starts; LUA_TOKEN makes the administrative groups
                    // deny-only without having to enumerate them.
                    plan.Flags = CreateRestrictedTokenFlags.DISABLE_MAX_PRIVILEGE
                                 | CreateRestrictedTokenFlags.LUA_TOKEN
                                 | CreateRestrictedTokenFlags.WRITE_RESTRICTED;
                    plan.RestrictingSids.Add(unique);
                    AddIfPresent(plan.RestrictingSids, logonSid);
                    plan.RestrictingSids.Add(SidFactory.ToBinary(SidFactory.Everyone));
                    break;

                case TokenLevel.StrictlyRestricted:
                    plan.Flags = CreateRestrictedTokenFlags.DISABLE_MAX_PRIVILEGE;
                    plan.DenyOnlySids.AddRange(AllGroupsExcept(groups));
                    plan.RestrictingSids.Add(SidFactory.ToBinary(SidFactory.Restricted));
                    AddIfPresent(plan.RestrictingSids, logonSid);
                    plan.RestrictingSids.Add(unique);
                    break;

                case TokenLevel.Lockdown:
                    plan.Flags = CreateRestrictedTokenFlags.DISABLE_MAX_PRIVILEGE;
                    plan.DenyOnlySids.AddRange(AllGroupsExcept(groups));
                    plan.DenyOnlySids.Add(userSid);
                    plan.RestrictingSids.Add(SidFactory.ToBinary(SidFactory.Null));
                    plan.RestrictingSids.Add(unique);
                    plan.RemoveEveryPrivilege = true;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown token level.");
            }

            return plan;
        }

        /// <summary>
        /// Returns every group in the token that should become deny-only. The integrity label and the
        /// logon SID are always skipped: the first is not a group in the access-check sense and the second
        /// is what lets the process create objects in its own BaseNamedObjects directory.
        /// </summary>
        private static IEnumerable<byte[]> AllGroupsExcept(IReadOnlyList<TokenGroup> groups, params SecurityIdentifier[] exceptions)
        {
            var exceptionSids = new List<string>(exceptions.Length);
            foreach (var exception in exceptions)
            {
                exceptionSids.Add(exception.Value);
            }

            foreach (var group in groups)
            {
                if (group.IsIntegrity || group.IsLogonId)
                {
                    continue;
                }

                if (exceptionSids.Contains(group.Sid.Value))
                {
                    continue;
                }

                yield return group.Binary;
            }
        }

        private static void AddIfPresent(List<byte[]> target, byte[]? sid)
        {
            if (sid != null)
            {
                target.Add(sid);
            }
        }

        private static SafeTokenHandle OpenCurrentProcessToken()
        {
            const uint DesiredAccess = NativeMethods.TOKEN_DUPLICATE
                                       | NativeMethods.TOKEN_ASSIGN_PRIMARY
                                       | NativeMethods.TOKEN_QUERY
                                       | NativeMethods.TOKEN_ADJUST_DEFAULT
                                       | NativeMethods.TOKEN_ADJUST_PRIVILEGES;

            if (!NativeMethods.OpenProcessToken(NativeMethods.GetCurrentProcess(), DesiredAccess, out var token))
            {
                throw SandboxException.FromLastWin32Error(SandboxStep.OpenProcessToken);
            }

            return token;
        }

        private static SafeTokenHandle CreateRestrictedToken(SafeTokenHandle processToken, TokenPlan plan)
        {
            var pinned = new List<GCHandle>();
            try
            {
                var deny = PinSids(plan.DenyOnlySids, pinned, NativeMethods.SE_GROUP_USE_FOR_DENY_ONLY);
                var restrict = PinSids(plan.RestrictingSids, pinned, 0);

                if (!NativeMethods.CreateRestrictedToken(
                        processToken,
                        plan.Flags,
                        deny.Length,
                        deny.Length == 0 ? null : deny,
                        0,
                        null,
                        restrict.Length,
                        restrict.Length == 0 ? null : restrict,
                        out var restrictedToken))
                {
                    throw SandboxException.FromLastWin32Error(SandboxStep.CreateRestrictedToken);
                }

                return restrictedToken;
            }
            finally
            {
                foreach (var handle in pinned)
                {
                    handle.Free();
                }
            }
        }

        private static SidAndAttributes[] PinSids(List<byte[]> sids, List<GCHandle> pinned, uint attributes)
        {
            var result = new SidAndAttributes[sids.Count];
            for (var i = 0; i < sids.Count; i++)
            {
                var handle = GCHandle.Alloc(sids[i], GCHandleType.Pinned);
                pinned.Add(handle);
                result[i] = new SidAndAttributes
                {
                    Sid = handle.AddrOfPinnedObject(),
                    Attributes = attributes,
                };
            }

            return result;
        }

        private static IReadOnlyList<TokenGroup> ReadTokenGroups(SafeTokenHandle token)
        {
            var buffer = QueryTokenInformation(token, TokenInformationClass.TokenGroups);
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                var basePointer = handle.AddrOfPinnedObject();
                var groupCount = Marshal.ReadInt32(basePointer);
                var entrySize = Marshal.SizeOf<SidAndAttributes>();
                var groups = new List<TokenGroup>(groupCount);

                // TOKEN_GROUPS is { DWORD GroupCount; SID_AND_ATTRIBUTES Groups[]; } and the array starts at
                // the natural alignment of SID_AND_ATTRIBUTES, which is the pointer size.
                for (var i = 0; i < groupCount; i++)
                {
                    var entry = Marshal.PtrToStructure<SidAndAttributes>(basePointer + IntPtr.Size + (i * entrySize));
                    groups.Add(new TokenGroup(CopySid(entry.Sid), entry.Attributes));
                }

                return groups;
            }
            finally
            {
                handle.Free();
            }
        }

        private static byte[] ReadTokenUser(SafeTokenHandle token)
        {
            var buffer = QueryTokenInformation(token, TokenInformationClass.TokenUser);
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                var user = Marshal.PtrToStructure<SidAndAttributes>(handle.AddrOfPinnedObject());
                return CopySid(user.Sid);
            }
            finally
            {
                handle.Free();
            }
        }

        private static byte[]? FindLogonSid(IReadOnlyList<TokenGroup> groups)
        {
            foreach (var group in groups)
            {
                if (group.IsLogonId)
                {
                    return group.Binary;
                }
            }

            return null;
        }

        private static byte[] QueryTokenInformation(SafeTokenHandle token, TokenInformationClass informationClass)
        {
            NativeMethods.GetTokenInformation(token, informationClass, IntPtr.Zero, 0, out var length);
            if (length == 0)
            {
                throw SandboxException.FromLastWin32Error(SandboxStep.QueryTokenInformation, informationClass.ToString());
            }

            var buffer = new byte[length];
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                if (!NativeMethods.GetTokenInformation(token, informationClass, handle.AddrOfPinnedObject(), length, out length))
                {
                    throw SandboxException.FromLastWin32Error(SandboxStep.QueryTokenInformation, informationClass.ToString());
                }
            }
            finally
            {
                handle.Free();
            }

            return buffer;
        }

        private static byte[] CopySid(IntPtr sid)
        {
            var length = NativeMethods.GetLengthSid(sid);
            var bytes = new byte[length];
            Marshal.Copy(sid, bytes, 0, length);
            return bytes;
        }

        private static void RemoveAllPrivileges(SafeTokenHandle token)
        {
            // DISABLE_MAX_PRIVILEGE keeps SeChangeNotifyPrivilege; a lockdown token drops even that, which
            // is what makes directory traversal fail closed.
            if (!NativeMethods.AdjustTokenPrivileges(token, true, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero))
            {
                throw SandboxException.FromLastWin32Error(SandboxStep.AdjustTokenPrivileges);
            }
        }

        /// <summary>
        /// Narrows the token's default DACL to the token user and the unique per-run SID.
        /// <para>
        /// Every kernel object the sandboxed process creates without an explicit security descriptor
        /// inherits this DACL. The logon SID is deliberately <em>not</em> in it: leaving it there would let
        /// any process in the same logon session - including another sandboxed run, whose restricting SIDs
        /// also contain the logon SID - open those objects. Dropping it is what makes two concurrent runs
        /// unable to reach each other, because the only shared identity left, the token user, is not one of
        /// the restricting SIDs that the second access check evaluates.
        /// </para>
        /// <para>
        /// This does not stop the process creating objects in its own BaseNamedObjects directory: that
        /// needs the logon SID to be a group on the token, which it still is.
        /// </para>
        /// </summary>
        private static void SetDefaultDacl(SafeTokenHandle token, byte[] userSid, SecurityIdentifier uniqueRunSid)
        {
            var dacl = new DiscretionaryAcl(false, false, 2);
            dacl.SetAccess(AccessControlType.Allow, new SecurityIdentifier(userSid, 0), GenericAll, InheritanceFlags.None, PropagationFlags.None);
            dacl.SetAccess(AccessControlType.Allow, uniqueRunSid, GenericAll, InheritanceFlags.None, PropagationFlags.None);

            var aclBytes = new byte[dacl.BinaryLength];
            dacl.GetBinaryForm(aclBytes, 0);

            var aclPointer = Marshal.AllocHGlobal(aclBytes.Length);
            var infoPointer = Marshal.AllocHGlobal(IntPtr.Size);
            try
            {
                Marshal.Copy(aclBytes, 0, aclPointer, aclBytes.Length);
                Marshal.WriteIntPtr(infoPointer, aclPointer);

                if (!NativeMethods.SetTokenInformation(token, TokenInformationClass.TokenDefaultDacl, infoPointer, IntPtr.Size))
                {
                    throw SandboxException.FromLastWin32Error(SandboxStep.SetTokenDefaultDacl);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(infoPointer);
                Marshal.FreeHGlobal(aclPointer);
            }
        }

        private static void SetIntegrityLevel(SafeTokenHandle token, IntegrityLevel integrityLevel)
        {
            var integritySid = new SecurityIdentifier("S-1-16-" + ((int)integrityLevel).ToString(System.Globalization.CultureInfo.InvariantCulture));
            var sidBytes = SidFactory.ToBinary(integritySid);

            var sidPointer = Marshal.AllocHGlobal(sidBytes.Length);
            var labelPointer = Marshal.AllocHGlobal(Marshal.SizeOf<SidAndAttributes>());
            try
            {
                Marshal.Copy(sidBytes, 0, sidPointer, sidBytes.Length);
                Marshal.StructureToPtr(
                    new SidAndAttributes { Sid = sidPointer, Attributes = NativeMethods.SE_GROUP_INTEGRITY },
                    labelPointer,
                    false);

                if (!NativeMethods.SetTokenInformation(
                        token,
                        TokenInformationClass.TokenIntegrityLevel,
                        labelPointer,
                        Marshal.SizeOf<SidAndAttributes>() + sidBytes.Length))
                {
                    throw SandboxException.FromLastWin32Error(SandboxStep.SetTokenIntegrityLevel, integrityLevel.ToString());
                }
            }
            finally
            {
                Marshal.FreeHGlobal(labelPointer);
                Marshal.FreeHGlobal(sidPointer);
            }
        }

        /// <summary>
        /// Adds no-read-up and no-execute-up to the token's own mandatory label, so a process at a lower
        /// integrity level cannot open the token to duplicate or impersonate it. Mirrors Chromium's
        /// HardenTokenIntegrityLevelPolicy.
        /// </summary>
        private static bool TryHardenIntegrityPolicy(SafeTokenHandle token, IntegrityLevel integrityLevel)
        {
            var integritySid = new SecurityIdentifier(
                "S-1-16-" + ((int)integrityLevel).ToString(System.Globalization.CultureInfo.InvariantCulture));
            var sidBytes = SidFactory.ToBinary(integritySid);

            var mask = SystemMandatoryLabelNoWriteUp | SystemMandatoryLabelNoReadUp | SystemMandatoryLabelNoExecuteUp;
            var opaque = new byte[sizeof(uint) + sidBytes.Length];
            BitConverter.GetBytes(mask).CopyTo(opaque, 0);
            sidBytes.CopyTo(opaque, sizeof(uint));

            var sacl = new RawAcl(GenericAcl.AclRevision, 1);
            sacl.InsertAce(0, new CustomAce((AceType)SystemMandatoryLabelAceType, AceFlags.None, opaque));

            var aclBytes = new byte[sacl.BinaryLength];
            sacl.GetBinaryForm(aclBytes, 0);

            var aclHandle = GCHandle.Alloc(aclBytes, GCHandleType.Pinned);
            try
            {
                return NativeMethods.SetSecurityInfo(
                    token.DangerousGetHandle(),
                    NativeMethods.SE_KERNEL_OBJECT,
                    LabelSecurityInformation,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    aclHandle.AddrOfPinnedObject()) == 0;
            }
            finally
            {
                aclHandle.Free();
            }
        }

        private readonly struct TokenGroup
        {
            public TokenGroup(byte[] binary, uint attributes)
            {
                this.Binary = binary;
                this.Attributes = attributes;
                this.Sid = new SecurityIdentifier(binary, 0);
            }

            public byte[] Binary { get; }

            public uint Attributes { get; }

            public SecurityIdentifier Sid { get; }

            public bool IsIntegrity => (this.Attributes & NativeMethods.SE_GROUP_INTEGRITY) != 0;

            public bool IsLogonId => (this.Attributes & NativeMethods.SE_GROUP_LOGON_ID) == NativeMethods.SE_GROUP_LOGON_ID;
        }

        private sealed class TokenPlan
        {
            public CreateRestrictedTokenFlags Flags { get; set; }

            public List<byte[]> DenyOnlySids { get; } = new List<byte[]>();

            public List<byte[]> RestrictingSids { get; } = new List<byte[]>();

            public bool RemoveEveryPrivilege { get; set; }
        }
    }
}
