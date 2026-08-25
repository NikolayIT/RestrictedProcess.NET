// <copyright file="LimitViolationInformation.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.JobObjects
{
    using System.Runtime.InteropServices;

    /// <summary>
    /// JOBOBJECT_LIMIT_VIOLATION_INFORMATION. Read after a JOB_OBJECT_MSG_NOTIFICATION_LIMIT message to
    /// find out which notification limit was exceeded, and by how much.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct LimitViolationInformation
    {
        public uint LimitFlags;
        public uint ViolationLimitFlags;
        public ulong IoReadBytes;
        public ulong IoReadBytesLimit;
        public ulong IoWriteBytes;
        public ulong IoWriteBytesLimit;
        public long PerJobUserTime;
        public long PerJobUserTimeLimit;
        public ulong JobMemory;
        public ulong JobMemoryLimit;
        public RateControlTolerance RateControlTolerance;
        public RateControlTolerance RateControlToleranceLimit;
    }
}
