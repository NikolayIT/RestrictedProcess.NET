// <copyright file="WritableDirectoryGrant.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.Process
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.AccessControl;
    using System.Security.Principal;

    /// <summary>
    /// Grants the unique per-run SID write access to the directories a run is allowed to write to, and
    /// takes the grant away again when the run ends.
    /// <para>
    /// This is the other half of <see cref="TokenLevel.WriteRestricted"/>. That token level makes the
    /// restricting SIDs apply to write access only, so the process can read whatever it needs to start but
    /// can only write where its own SID has been granted. Because the SID is generated per execution, the
    /// grant is meaningful for exactly one run: two concurrent runs cannot write into each other's
    /// directories even if they are given the same path.
    /// </para>
    /// </summary>
    internal sealed class WritableDirectoryGrant : IDisposable
    {
        private readonly SecurityIdentifier sid;
        private readonly List<string> granted = new List<string>();

        public WritableDirectoryGrant(SecurityIdentifier sid, IEnumerable<string> directories)
        {
            this.sid = sid;

            try
            {
                foreach (var directory in directories)
                {
                    if (string.IsNullOrWhiteSpace(directory))
                    {
                        continue;
                    }

                    var fullPath = Path.GetFullPath(directory);
                    if (!Directory.Exists(fullPath))
                    {
                        throw SandboxException.For(
                            SandboxStep.BuildSecurityDescriptor,
                            "The writable directory " + fullPath + " does not exist.");
                    }

                    this.Modify(fullPath, grant: true);
                    this.granted.Add(fullPath);
                }
            }
            catch
            {
                this.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            foreach (var path in this.granted)
            {
                try
                {
                    this.Modify(path, grant: false);
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (IOException)
                {
                }
            }

            this.granted.Clear();
        }

        private void Modify(string path, bool grant)
        {
            var directory = new DirectoryInfo(path);
            var security = directory.GetAccessControl(AccessControlSections.Access);
            var rule = new FileSystemAccessRule(
                this.sid,
                FileSystemRights.Modify,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow);

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
    }
}
