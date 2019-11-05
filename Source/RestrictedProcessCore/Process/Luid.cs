// <copyright file="Luid.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

using System.Runtime.InteropServices;

namespace RestrictedProcessCore.Process
{
    /// <summary>
    /// An LUID is a 64-bit value guaranteed to be unique only on the system on which it was generated. The uniqueness of a locally unique identifier (LUID) is guaranteed only until the system is restarted.
    /// Applications must use functions and structures to manipulate LUID values.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Luid
    {
        public uint LowPart;

        public long HighPart;
    }
}
