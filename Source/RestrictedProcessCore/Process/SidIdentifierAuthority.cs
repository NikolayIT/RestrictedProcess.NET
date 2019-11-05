// <copyright file="SidIdentifierAuthority.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

using System.Runtime.InteropServices;

namespace RestrictedProcessCore.Process
{
    /// <summary>
    /// The SidIdentifierAuthority structure represents the top-level authority of a security identifier (SID).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SidIdentifierAuthority
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6, ArraySubType = UnmanagedType.I1)]
        public byte[] Value;

        public SidIdentifierAuthority(byte[] value)
        {
            this.Value = value;
        }
    }
}
