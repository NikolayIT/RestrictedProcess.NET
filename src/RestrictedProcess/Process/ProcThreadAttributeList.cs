// <copyright file="ProcThreadAttributeList.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.Process
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Wraps a PROC_THREAD_ATTRIBUTE_LIST used to pass extended attributes to CreateProcessAsUser through
    /// a STARTUPINFOEX structure.
    /// <para>
    /// The attribute values are unmanaged copies owned by this object: UpdateProcThreadAttribute stores
    /// the pointer, not the contents, so every buffer has to stay alive until CreateProcessAsUser has
    /// returned. That is why this object must not be disposed before the call completes.
    /// </para>
    /// </summary>
    internal sealed class ProcThreadAttributeList : IDisposable
    {
        private const uint ProcThreadAttributeHandleList = 0x20002;
        private const uint ProcThreadAttributeMitigationPolicy = 0x20007;
        private const uint ProcThreadAttributeSecurityCapabilities = 0x20009;
        private const uint ProcThreadAttributeJobList = 0x2000D;
        private const uint ProcThreadAttributeChildProcessPolicy = 0x2000E;
        private const uint ProcThreadAttributeAllApplicationPackagesPolicy = 0x2000F;
        private const uint ProcThreadAttributeDesktopAppPolicy = 0x20012;

        private const int ChildProcessPolicyRestricted = 0x1;
        private const int AllApplicationPackagesOptOut = 0x1;
        private const int DesktopAppBreakawayDisableProcessTree = 0x2;

        private readonly List<IntPtr> attributeValues = new List<IntPtr>();
        private IntPtr attributeList;

        public ProcThreadAttributeList(int attributeCount)
        {
            var size = IntPtr.Zero;
            NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, attributeCount, 0, ref size);
            this.attributeList = Marshal.AllocHGlobal(size);
            if (!NativeMethods.InitializeProcThreadAttributeList(this.attributeList, attributeCount, 0, ref size))
            {
                Marshal.FreeHGlobal(this.attributeList);
                this.attributeList = IntPtr.Zero;
                throw SandboxException.FromLastWin32Error(SandboxStep.InitializeAttributeList);
            }
        }

        public IntPtr Pointer => this.attributeList;

        /// <summary>
        /// Restricts handle inheritance to exactly the given handles. Every handle in the list must be
        /// inheritable and the process must be created with bInheritHandles set to true.
        /// </summary>
        /// <param name="handles">The only handles the child is allowed to inherit.</param>
        public void SetHandleList(IntPtr[] handles)
        {
            var value = this.Allocate(handles.Length * IntPtr.Size);
            Marshal.Copy(handles, 0, value, handles.Length);
            this.UpdateAttribute(ProcThreadAttributeHandleList, value, handles.Length * IntPtr.Size);
        }

        /// <summary>
        /// Places the process in the given job objects at creation time. This is strictly better than
        /// creating the process suspended and calling AssignProcessToJobObject afterwards: the limits are
        /// in force from the first instruction, and there is no window in which the process exists outside
        /// the job.
        /// </summary>
        /// <param name="jobHandles">The jobs to attach the new process to.</param>
        public void SetJobList(IntPtr[] jobHandles)
        {
            var value = this.Allocate(jobHandles.Length * IntPtr.Size);
            Marshal.Copy(jobHandles, 0, value, jobHandles.Length);
            this.UpdateAttribute(ProcThreadAttributeJobList, value, jobHandles.Length * IntPtr.Size);
        }

        /// <summary>
        /// Applies the process creation mitigation policies. When any second-word policy is requested the
        /// attribute is 16 bytes wide, which Windows 10 1703 and later understand; older builds reject
        /// that size, so the call falls back to the 8-byte form rather than failing the launch.
        /// </summary>
        /// <param name="policy">The first policy word.</param>
        /// <param name="policy2">The second policy word, or zero.</param>
        public void SetMitigationPolicy(ulong policy, ulong policy2)
        {
            if (policy2 != 0)
            {
                var wideValue = this.Allocate(sizeof(ulong) * 2);
                Marshal.WriteInt64(wideValue, 0, unchecked((long)policy));
                Marshal.WriteInt64(wideValue, sizeof(ulong), unchecked((long)policy2));

                if (this.TryUpdateAttribute(ProcThreadAttributeMitigationPolicy, wideValue, sizeof(ulong) * 2))
                {
                    return;
                }
            }

            var value = this.Allocate(sizeof(ulong));
            Marshal.WriteInt64(value, unchecked((long)policy));
            this.UpdateAttribute(ProcThreadAttributeMitigationPolicy, value, sizeof(ulong));
        }

        /// <summary>
        /// Runs the process inside the AppContainer identified by the given SID, with no capabilities. The
        /// Windows Firewall then blocks the process from reaching the network.
        /// </summary>
        /// <param name="appContainerSid">The SID of the AppContainer to launch into.</param>
        public void SetSecurityCapabilities(IntPtr appContainerSid)
        {
            var size = Marshal.SizeOf<SecurityCapabilities>();
            var value = this.Allocate(size);
            Marshal.StructureToPtr(
                new SecurityCapabilities
                {
                    AppContainerSid = appContainerSid,
                    Capabilities = IntPtr.Zero,
                    CapabilityCount = 0,
                    Reserved = 0,
                },
                value,
                false);
            this.UpdateAttribute(ProcThreadAttributeSecurityCapabilities, value, size);
        }

        /// <summary>
        /// Turns the AppContainer into a Less Privileged AppContainer: the process stops being granted
        /// access through the ALL APPLICATION PACKAGES identity, so it reaches only what is granted to ALL
        /// RESTRICTED APPLICATION PACKAGES or to its own package SID. This is the configuration Chromium
        /// uses for its most locked-down processes.
        /// </summary>
        public void SetLowPrivilegeAppContainer()
        {
            var value = this.Allocate(sizeof(int));
            Marshal.WriteInt32(value, AllApplicationPackagesOptOut);
            this.UpdateAttribute(ProcThreadAttributeAllApplicationPackagesPolicy, value, sizeof(int));
        }

        /// <summary>
        /// Prevents the process from creating child processes at the kernel level (CreateProcess fails
        /// with ERROR_CHILD_PROCESS_BLOCKED).
        /// </summary>
        public void SetChildProcessRestricted()
        {
            var value = this.Allocate(sizeof(int));
            Marshal.WriteInt32(value, ChildProcessPolicyRestricted);
            this.UpdateAttribute(ProcThreadAttributeChildProcessPolicy, value, sizeof(int));
        }

        /// <summary>
        /// Keeps shell-brokered launches inside the process tree. Without this, starting a packaged-app
        /// alias such as notepad.exe is carried out by a system service, so the new process is created
        /// outside the job object and neither the active-process limit nor the job kill reaches it.
        /// </summary>
        public void SetDesktopAppBreakawayDisabled()
        {
            var value = this.Allocate(sizeof(int));
            Marshal.WriteInt32(value, DesktopAppBreakawayDisableProcessTree);
            this.UpdateAttribute(ProcThreadAttributeDesktopAppPolicy, value, sizeof(int));
        }

        public void Dispose()
        {
            if (this.attributeList != IntPtr.Zero)
            {
                NativeMethods.DeleteProcThreadAttributeList(this.attributeList);
                Marshal.FreeHGlobal(this.attributeList);
                this.attributeList = IntPtr.Zero;
            }

            foreach (var value in this.attributeValues)
            {
                Marshal.FreeHGlobal(value);
            }

            this.attributeValues.Clear();
        }

        private IntPtr Allocate(int size)
        {
            var value = Marshal.AllocHGlobal(size);
            this.attributeValues.Add(value);
            return value;
        }

        private void UpdateAttribute(uint attribute, IntPtr value, int size)
        {
            if (!this.TryUpdateAttribute(attribute, value, size))
            {
                throw SandboxException.FromLastWin32Error(
                    SandboxStep.UpdateAttributeList,
                    "attribute 0x" + attribute.ToString("X", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        private bool TryUpdateAttribute(uint attribute, IntPtr value, int size)
        {
            return NativeMethods.UpdateProcThreadAttribute(
                this.attributeList,
                0,
                (IntPtr)attribute,
                value,
                (IntPtr)size,
                IntPtr.Zero,
                IntPtr.Zero);
        }
    }
}
