// <copyright file="SecurityAttributes.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.Process
{
    using System;
    using System.Runtime.InteropServices;

    /// <summary>
    /// SECURITY_ATTRIBUTES. Use <see cref="Create"/> rather than the default value: nLength has to be the
    /// real size of the structure, which is 24 bytes on x64 and 12 on x86, and Windows validates it for
    /// some of the APIs that take this structure.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SecurityAttributes
    {
        public int Length;

        public IntPtr SecurityDescriptor;

        [MarshalAs(UnmanagedType.Bool)]
        public bool InheritHandle;

        public static SecurityAttributes Create(bool inheritHandle = false, IntPtr securityDescriptor = default)
        {
            return new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                SecurityDescriptor = securityDescriptor,
                InheritHandle = inheritHandle,
            };
        }
    }
}
