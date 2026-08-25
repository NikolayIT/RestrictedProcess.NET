// <copyright file="SandboxDesktop.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.Process
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Runtime.InteropServices;
    using System.Security.AccessControl;
    using System.Security.Principal;
    using System.Text;

    /// <summary>
    /// A throwaway desktop the sandboxed process runs on, so it cannot enumerate, read or send window
    /// messages to windows on the interactive desktop.
    /// <para>
    /// The DACL is built explicitly rather than granting everyone full control. Access is given only to
    /// the identities the sandboxed token actually presents - the logon SID, which survives as an enabled
    /// group at every token level, and the unique per-run SID, which is what the restricting-SID check
    /// matches against - and the rights that let a process hook, journal-record, switch away from or
    /// re-permission the desktop are denied outright, mirroring the deny mask the Chromium sandbox applies.
    /// </para>
    /// <para>
    /// The mandatory label follows the configured integrity level. Hardcoding it to Low would leave an
    /// Untrusted process unable to attach to its own desktop.
    /// </para>
    /// </summary>
    internal sealed class SandboxDesktop : IDisposable
    {
        /// <summary>
        /// The rights a process needs to live on a desktop: create and draw windows and menus, read the
        /// desktop object, and read its security. Deliberately excludes everything in
        /// <see cref="DangerousRights"/>.
        /// </summary>
        private const uint RequiredRights = NativeMethods.DESKTOP_READOBJECTS
                                            | NativeMethods.DESKTOP_CREATEWINDOW
                                            | NativeMethods.DESKTOP_CREATEMENU
                                            | NativeMethods.DESKTOP_ENUMERATE
                                            | NativeMethods.DESKTOP_WRITEOBJECTS
                                            | NativeMethods.STANDARD_READ_CONTROL;

        /// <summary>
        /// The rights that turn a shared desktop into an attack surface: global hooks and journal
        /// record/playback (keylogging), switching the visible desktop, and taking ownership or rewriting
        /// the DACL to undo any of the above.
        /// </summary>
        private const uint DangerousRights = NativeMethods.DESKTOP_HOOKCONTROL
                                             | NativeMethods.DESKTOP_JOURNALRECORD
                                             | NativeMethods.DESKTOP_JOURNALPLAYBACK
                                             | NativeMethods.DESKTOP_SWITCHDESKTOP
                                             | NativeMethods.STANDARD_WRITE_DAC
                                             | NativeMethods.STANDARD_WRITE_OWNER
                                             | NativeMethods.STANDARD_DELETE;

        private const byte SystemMandatoryLabelAceType = 0x11;

        private const uint GenericAll = 0x10000000;

        private const uint CreateDesktopAccess = NativeMethods.DESKTOP_READOBJECTS
                                                 | NativeMethods.DESKTOP_CREATEWINDOW
                                                 | NativeMethods.STANDARD_READ_CONTROL
                                                 | NativeMethods.STANDARD_WRITE_DAC
                                                 | NativeMethods.STANDARD_WRITE_OWNER;

        private static readonly object WindowStationSwitchLock = new object();

        private IntPtr desktopHandle;
        private IntPtr windowStationHandle;

        public SandboxDesktop(
            IntegrityLevel integrityLevel,
            SecurityIdentifier ownerSid,
            IReadOnlyList<SecurityIdentifier> allowedSids,
            bool createWindowStation)
        {
            var securityDescriptor = BuildSecurityDescriptor(ownerSid, allowedSids);
            var descriptorHandle = GCHandle.Alloc(securityDescriptor, GCHandleType.Pinned);
            try
            {
                var attributes = SecurityAttributes.Create(false, descriptorHandle.AddrOfPinnedObject());
                var name = "rp_" + Guid.NewGuid().ToString("N");

                if (createWindowStation)
                {
                    this.windowStationHandle = NativeMethods.CreateWindowStation(
                        null, 0, NativeMethods.WINSTA_CREATEDESKTOP | NativeMethods.GENERIC_READ, ref attributes);
                    if (this.windowStationHandle == IntPtr.Zero)
                    {
                        throw SandboxException.FromLastWin32Error(SandboxStep.CreateWindowStation);
                    }
                }

                // Creating a desktop always targets the window station the calling *process* is attached
                // to, so making one on an alternate station means switching there and back. The switch is
                // process-wide, hence the lock, and it is why the alternate window station is opt-in.
                lock (WindowStationSwitchLock)
                {
                    var previousStation = IntPtr.Zero;
                    if (this.windowStationHandle != IntPtr.Zero)
                    {
                        previousStation = NativeMethods.GetProcessWindowStation();
                        if (!NativeMethods.SetProcessWindowStation(this.windowStationHandle))
                        {
                            throw SandboxException.FromLastWin32Error(SandboxStep.CreateWindowStation, "SetProcessWindowStation");
                        }
                    }

                    try
                    {
                        this.desktopHandle = NativeMethods.CreateDesktop(
                            name, IntPtr.Zero, IntPtr.Zero, 0, CreateDesktopAccess, ref attributes);
                        if (this.desktopHandle == IntPtr.Zero)
                        {
                            throw SandboxException.FromLastWin32Error(SandboxStep.CreateDesktop, name);
                        }
                    }
                    finally
                    {
                        if (previousStation != IntPtr.Zero)
                        {
                            NativeMethods.SetProcessWindowStation(previousStation);
                        }
                    }
                }

                this.ApplyMandatoryLabel(integrityLevel);

                // Always fully qualify as "station\desktop". An unqualified name is resolved relative to
                // the window station the child is attached to, which is not the parent's for an
                // AppContainer process: it then fails to find the desktop and dies during user32
                // initialisation with ERROR_DLL_INIT_FAILED.
                var station = this.windowStationHandle == IntPtr.Zero
                    ? NativeMethods.GetProcessWindowStation()
                    : this.windowStationHandle;
                this.Name = GetObjectName(station) + "\\" + name;
            }
            catch
            {
                this.Dispose();
                throw;
            }
            finally
            {
                descriptorHandle.Free();
            }
        }

        ~SandboxDesktop()
        {
            this.Dispose();
        }

        /// <summary>
        /// Gets the value to put in STARTUPINFO.lpDesktop: either the desktop name on the current window
        /// station, or a fully qualified "station\desktop" name.
        /// </summary>
        public string Name { get; } = string.Empty;

        public void Dispose()
        {
            if (this.desktopHandle != IntPtr.Zero)
            {
                NativeMethods.CloseDesktop(this.desktopHandle);
                this.desktopHandle = IntPtr.Zero;
            }

            if (this.windowStationHandle != IntPtr.Zero)
            {
                NativeMethods.CloseWindowStation(this.windowStationHandle);
                this.windowStationHandle = IntPtr.Zero;
            }

            GC.SuppressFinalize(this);
        }

        private static byte[] BuildSecurityDescriptor(SecurityIdentifier ownerSid, IReadOnlyList<SecurityIdentifier> allowedSids)
        {
            var dacl = new DiscretionaryAcl(false, false, allowedSids.Count + 1);

            // Defence in depth: nothing below grants these rights, but an explicit deny for the RESTRICTED
            // identity means they stay denied even if the DACL is ever widened.
            dacl.SetAccess(
                AccessControlType.Deny,
                SidFactory.Restricted,
                unchecked((int)DangerousRights),
                InheritanceFlags.None,
                PropagationFlags.None);

            // The host keeps full control of the desktop it owns: it has to be able to apply the mandatory
            // label afterwards, which needs WRITE_OWNER, and to tear the desktop down. Leaving this out is
            // what silently produced an unlabelled desktop that a low integrity process could not attach to.
            dacl.SetAccess(
                AccessControlType.Allow,
                ownerSid,
                unchecked((int)GenericAll),
                InheritanceFlags.None,
                PropagationFlags.None);

            foreach (var sid in allowedSids)
            {
                if (sid == ownerSid)
                {
                    continue;
                }

                dacl.SetAccess(
                    AccessControlType.Allow,
                    sid,
                    unchecked((int)RequiredRights),
                    InheritanceFlags.None,
                    PropagationFlags.None);
            }

            // The label cannot ride along here: supplying any SACL to CreateDesktop is treated as setting
            // an audit SACL and fails with ERROR_PRIVILEGE_NOT_HELD. It is applied afterwards through
            // LABEL_SECURITY_INFORMATION, which is the one path that does not need SeSecurityPrivilege.
            var descriptor = new RawSecurityDescriptor(
                ControlFlags.DiscretionaryAclPresent,
                null,
                null,
                new RawAcl(GetBinaryForm(dacl), 0),
                null);

            var bytes = new byte[descriptor.BinaryLength];
            descriptor.GetBinaryForm(bytes, 0);
            return bytes;
        }

        private static byte[] GetBinaryForm(DiscretionaryAcl acl)
        {
            var bytes = new byte[acl.BinaryLength];
            acl.GetBinaryForm(bytes, 0);
            return bytes;
        }

        private static string GetObjectName(IntPtr handle)
        {
            NativeMethods.GetUserObjectInformation(handle, NativeMethods.UOI_NAME, System.Array.Empty<byte>(), 0, out var needed);
            if (needed <= 0)
            {
                throw SandboxException.FromLastWin32Error(SandboxStep.CreateWindowStation, "GetUserObjectInformation");
            }

            var buffer = new byte[needed];
            if (!NativeMethods.GetUserObjectInformation(handle, NativeMethods.UOI_NAME, buffer, buffer.Length, out needed))
            {
                throw SandboxException.FromLastWin32Error(SandboxStep.CreateWindowStation, "GetUserObjectInformation");
            }

            return Encoding.Unicode.GetString(buffer).TrimEnd('\0');
        }

        /// <summary>
        /// Builds the mandatory label for the desktop at the configured integrity level. Deriving the level
        /// matters as much as applying it: a hardcoded Low label leaves an Untrusted process unable to use
        /// its own desktop.
        /// </summary>
        private static RawAcl BuildMandatoryLabel(IntegrityLevel integrityLevel)
        {
            var integritySid = new SecurityIdentifier(
                "S-1-16-" + ((int)integrityLevel).ToString(CultureInfo.InvariantCulture));
            var sidBytes = SidFactory.ToBinary(integritySid);

            // SYSTEM_MANDATORY_LABEL_NO_WRITE_UP only: the sandboxed process must still be able to read its
            // own desktop, and blocking read-up would stop it attaching.
            var opaque = new byte[sizeof(uint) + sidBytes.Length];
            BitConverter.GetBytes(1u).CopyTo(opaque, 0);
            sidBytes.CopyTo(opaque, sizeof(uint));

            var sacl = new RawAcl(GenericAcl.AclRevision, 1);
            sacl.InsertAce(0, new CustomAce((AceType)SystemMandatoryLabelAceType, AceFlags.None, opaque));
            return sacl;
        }

        /// <summary>
        /// Writes the mandatory label with SetSecurityInfo. This has to be SetSecurityInfo and not
        /// SetUserObjectSecurity: the latter returns success for a LABEL_SECURITY_INFORMATION write on a
        /// desktop and then does not apply it, leaving an unlabelled - so implicitly Medium - desktop that a
        /// Low integrity process cannot attach to. The process then dies during user32 initialisation with
        /// ERROR_DLL_INIT_FAILED and no indication of why.
        /// </summary>
        private static void ApplyLabel(IntPtr handle, IntPtr sacl, IntegrityLevel integrityLevel)
        {
            var error = NativeMethods.SetSecurityInfo(
                handle,
                NativeMethods.SE_WINDOW_OBJECT,
                NativeMethods.LABEL_SECURITY_INFORMATION,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero,
                sacl);

            if (error != 0)
            {
                throw SandboxException.FromWin32Error(
                    SandboxStep.SetWindowObjectIntegrityLevel, error, integrityLevel.ToString());
            }
        }

        private static bool TryApplyLabel(IntPtr handle, IntPtr sacl)
        {
            return NativeMethods.SetSecurityInfo(
                handle,
                NativeMethods.SE_WINDOW_OBJECT,
                NativeMethods.LABEL_SECURITY_INFORMATION,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero,
                sacl) == 0;
        }

        private void ApplyMandatoryLabel(IntegrityLevel integrityLevel)
        {
            var sacl = BuildMandatoryLabel(integrityLevel);
            var aclBytes = new byte[sacl.BinaryLength];
            sacl.GetBinaryForm(aclBytes, 0);

            var aclHandle = GCHandle.Alloc(aclBytes, GCHandleType.Pinned);
            try
            {
                ApplyLabel(this.desktopHandle, aclHandle.AddrOfPinnedObject(), integrityLevel);

                if (this.windowStationHandle != IntPtr.Zero)
                {
                    // Best effort: labelling a window station needs rights the creator is not always
                    // granted, and the desktop label is the one that governs whether the process can
                    // attach.
                    TryApplyLabel(this.windowStationHandle, aclHandle.AddrOfPinnedObject());
                }
            }
            finally
            {
                aclHandle.Free();
            }
        }
    }
}
