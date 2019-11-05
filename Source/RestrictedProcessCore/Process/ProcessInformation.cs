// <copyright file="ProcessInformation.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

using System;
using System.Runtime.InteropServices;

namespace RestrictedProcessCore.Process
{
    /// <summary>
    /// Contains information about a newly created process and its primary thread.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public int ProcessId;
        public int ThreadId;
    }
}
