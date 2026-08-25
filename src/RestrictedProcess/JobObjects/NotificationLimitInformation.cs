// <copyright file="NotificationLimitInformation.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.JobObjects
{
    using System.Runtime.InteropServices;

    /// <summary>
    /// JOBOBJECT_NOTIFICATION_LIMIT_INFORMATION. Unlike the hard limits in
    /// <see cref="ExtendedLimitInformation"/>, exceeding a notification limit neither fails an
    /// allocation nor terminates anything: it posts a JOB_OBJECT_MSG_NOTIFICATION_LIMIT message to the
    /// completion port associated with the job. That is precisely what the soft-limit design needs -
    /// the program keeps running past the limit so the overage stays measurable, but the breach is
    /// observed the instant it happens instead of at the next poll.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NotificationLimitInformation
    {
        public ulong IoReadBytesLimit;
        public ulong IoWriteBytesLimit;
        public long PerJobUserTimeLimit;
        public ulong JobMemoryLimit;
        public RateControlTolerance RateControlTolerance;
        public RateControlToleranceInterval RateControlToleranceInterval;
        public uint LimitFlags;
    }
}
