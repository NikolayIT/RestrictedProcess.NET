// <copyright file="SafeProcessHandle.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

using System;
using System.Diagnostics;
using System.Security;
using Microsoft.Win32.SafeHandles;

namespace RestrictedProcessCore.Process
{
    [SuppressUnmanagedCodeSecurity]
    internal sealed class SafeProcessHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private static SafeProcessHandle invalidHandle = new SafeProcessHandle(IntPtr.Zero);

        // Note that OpenProcess returns 0 on failure
        internal SafeProcessHandle()
            : base(true)
        {
        }

        internal SafeProcessHandle(IntPtr handle)
            : base(true)
        {
            this.SetHandle(handle);
        }

        internal void InitialSetHandle(IntPtr h)
        {
            Debug.Assert(this.IsInvalid, "Safe handle should only be set once");
            this.handle = h;
        }

        protected override bool ReleaseHandle()
        {
            return NativeMethods.CloseHandle(this.handle);
        }
    }
}
