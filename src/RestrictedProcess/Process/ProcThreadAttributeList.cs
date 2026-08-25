// <copyright file="ProcThreadAttributeList.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.Process
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Wraps a PROC_THREAD_ATTRIBUTE_LIST used to pass extended attributes
    /// (inheritable handle whitelist, mitigation policies, child process policy)
    /// to CreateProcessAsUser through a STARTUPINFOEX structure.
    /// The attribute values are unmanaged copies owned by this object, so the instance
    /// must stay alive (not disposed) until CreateProcessAsUser has returned.
    /// </summary>
    internal sealed class ProcThreadAttributeList : IDisposable
    {
        private const uint ProcThreadAttributeHandleList = 0x20002;
        private const uint ProcThreadAttributeMitigationPolicy = 0x20007;
        private const uint ProcThreadAttributeChildProcessPolicy = 0x2000E;

        private const int ChildProcessPolicyRestricted = 0x1;

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
                throw new Win32Exception();
            }
        }

        public IntPtr Pointer => this.attributeList;

        /// <summary>
        /// Restricts handle inheritance to exactly the given handles.
        /// Every handle in the list must be inheritable and the process must be
        /// created with bInheritHandles set to true.
        /// </summary>
        public void SetHandleList(IntPtr[] handles)
        {
            var value = Marshal.AllocHGlobal(handles.Length * IntPtr.Size);
            this.attributeValues.Add(value);
            Marshal.Copy(handles, 0, value, handles.Length);
            this.UpdateAttribute(ProcThreadAttributeHandleList, value, handles.Length * IntPtr.Size);
        }

        public void SetMitigationPolicy(ulong policy)
        {
            var value = Marshal.AllocHGlobal(sizeof(ulong));
            this.attributeValues.Add(value);
            Marshal.WriteInt64(value, unchecked((long)policy));
            this.UpdateAttribute(ProcThreadAttributeMitigationPolicy, value, sizeof(ulong));
        }

        /// <summary>
        /// Prevents the process from creating child processes at the kernel level
        /// (CreateProcess fails with ERROR_CHILD_PROCESS_BLOCKED).
        /// </summary>
        public void SetChildProcessRestricted()
        {
            var value = Marshal.AllocHGlobal(sizeof(int));
            this.attributeValues.Add(value);
            Marshal.WriteInt32(value, ChildProcessPolicyRestricted);
            this.UpdateAttribute(ProcThreadAttributeChildProcessPolicy, value, sizeof(int));
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

        private void UpdateAttribute(uint attribute, IntPtr value, int size)
        {
            if (!NativeMethods.UpdateProcThreadAttribute(
                    this.attributeList,
                    0,
                    (IntPtr)attribute,
                    value,
                    (IntPtr)size,
                    IntPtr.Zero,
                    IntPtr.Zero))
            {
                throw new Win32Exception();
            }
        }
    }
}
