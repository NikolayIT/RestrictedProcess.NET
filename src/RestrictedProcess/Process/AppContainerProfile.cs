// <copyright file="AppContainerProfile.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.Process
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Security.AccessControl;
    using System.Security.Principal;

    /// <summary>
    /// The AppContainer the sandboxed process runs in when network access is blocked. A process in an
    /// AppContainer with no capabilities has no <c>internetClient</c> capability, so the Windows Firewall
    /// denies its sockets.
    /// <para>
    /// The profile is registered once under a stable name and reused, rather than created and deleted on
    /// every execution: registering a profile writes to the registry and creates a directory under
    /// <c>%LOCALAPPDATA%\Packages</c>, which is both slow and easy to leak when a run is killed. Isolation
    /// between concurrent runs comes from the unique per-run SID in the token, not from the container.
    /// </para>
    /// <para>
    /// Access to the executable is granted to the container's own package SID and revoked again on
    /// dispose. Version 2 of this library granted ALL APPLICATION PACKAGES and never took it back, which
    /// left a permanent widening of the ACL on every executable it ever ran.
    /// </para>
    /// </summary>
    internal sealed class AppContainerProfile : IDisposable
    {
        private const int ErrorAlreadyExists = unchecked((int)0x800700B7);

        private static readonly object GrantLock = new object();
        private static readonly Dictionary<string, int> GrantReferenceCounts =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private readonly List<string> grantedPaths = new List<string>();

        private IntPtr sid;

        public AppContainerProfile(string profileName, IEnumerable<string> pathsToGrant)
        {
            this.Name = profileName;

            var hr = NativeMethods.CreateAppContainerProfile(profileName, profileName, profileName, null, 0, out this.sid);
            if (hr != 0)
            {
                if (hr != ErrorAlreadyExists)
                {
                    throw SandboxException.FromWin32Error(SandboxStep.CreateAppContainerProfile, hr, profileName);
                }

                // A profile from an earlier run is fine to reuse; a derived SID alone would not be, because
                // the OS refuses to launch into an AppContainer that has no registered profile.
                hr = NativeMethods.DeriveAppContainerSidFromAppContainerName(profileName, out this.sid);
                if (hr != 0)
                {
                    throw SandboxException.FromWin32Error(SandboxStep.CreateAppContainerProfile, hr, profileName);
                }
            }

            this.SecurityIdentifier = new SecurityIdentifier(this.sid);

            try
            {
                foreach (var path in pathsToGrant)
                {
                    this.Grant(path);
                }
            }
            catch
            {
                this.Dispose();
                throw;
            }
        }

        public string Name { get; }

        /// <summary>
        /// Gets the raw package SID, for PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES.
        /// </summary>
        public IntPtr Sid => this.sid;

        public SecurityIdentifier SecurityIdentifier { get; } = null!;

        /// <summary>
        /// Removes a registered profile. Not called during normal operation - the profile is deliberately
        /// long lived - but useful for cleaning up a machine.
        /// </summary>
        /// <param name="profileName">The profile to delete.</param>
        /// <returns>True when the profile was removed.</returns>
        public static bool DeleteProfile(string profileName)
        {
            return NativeMethods.DeleteAppContainerProfile(profileName) == 0;
        }

        public void Dispose()
        {
            foreach (var path in this.grantedPaths)
            {
                this.Revoke(path);
            }

            this.grantedPaths.Clear();

            if (this.sid != IntPtr.Zero)
            {
                NativeMethods.FreeSid(this.sid);
                this.sid = IntPtr.Zero;
            }
        }

        private static string KeyFor(string path, SecurityIdentifier sid)
        {
            return sid.Value + "|" + Path.GetFullPath(path);
        }

        private void Grant(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            lock (GrantLock)
            {
                var key = KeyFor(path, this.SecurityIdentifier);
                GrantReferenceCounts.TryGetValue(key, out var count);
                GrantReferenceCounts[key] = count + 1;
                this.grantedPaths.Add(path);

                if (count > 0)
                {
                    // Another execution in this process already granted it.
                    return;
                }

                try
                {
                    this.ModifyAccess(path, grant: true);
                }
                catch (UnauthorizedAccessException)
                {
                    // The executable may already be reachable by application packages, for example when it
                    // lives under a system directory. Losing the grant is not fatal; failing to start the
                    // sandbox because of it would be.
                }
                catch (IOException)
                {
                }
            }
        }

        private void Revoke(string path)
        {
            lock (GrantLock)
            {
                var key = KeyFor(path, this.SecurityIdentifier);
                if (!GrantReferenceCounts.TryGetValue(key, out var count))
                {
                    return;
                }

                if (count > 1)
                {
                    GrantReferenceCounts[key] = count - 1;
                    return;
                }

                GrantReferenceCounts.Remove(key);

                try
                {
                    this.ModifyAccess(path, grant: false);
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (IOException)
                {
                }
            }
        }

        private void ModifyAccess(string path, bool grant)
        {
            var isDirectory = Directory.Exists(path);
            var rights = FileSystemRights.ReadAndExecute;
            var inheritance = isDirectory
                ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit
                : InheritanceFlags.None;

            if (isDirectory)
            {
                var directory = new DirectoryInfo(path);
                var security = directory.GetAccessControl(AccessControlSections.Access);
                var rule = new FileSystemAccessRule(
                    this.SecurityIdentifier, rights, inheritance, PropagationFlags.None, AccessControlType.Allow);

                if (grant)
                {
                    security.AddAccessRule(rule);
                }
                else
                {
                    security.RemoveAccessRule(rule);
                }

                directory.SetAccessControl(security);
            }
            else
            {
                var file = new FileInfo(path);
                var security = file.GetAccessControl(AccessControlSections.Access);
                var rule = new FileSystemAccessRule(
                    this.SecurityIdentifier, rights, AccessControlType.Allow);

                if (grant)
                {
                    security.AddAccessRule(rule);
                }
                else
                {
                    security.RemoveAccessRule(rule);
                }

                file.SetAccessControl(security);
            }
        }
    }
}
