// <copyright file="BasicAccountingInformation.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.JobObjects
{
    using System.Runtime.InteropServices;

    /// <summary>
    /// JOBOBJECT_BASIC_ACCOUNTING_INFORMATION. The times cover every process ever associated with the
    /// job, including ones that have already exited, which makes this a strictly better source of
    /// processor time than GetProcessTimes on the root process alone.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BasicAccountingInformation
    {
        public long TotalUserTime;
        public long TotalKernelTime;
        public long ThisPeriodTotalUserTime;
        public long ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount;
        public uint TotalProcesses;
        public uint ActiveProcesses;
        public uint TotalTerminatedProcesses;
    }
}
