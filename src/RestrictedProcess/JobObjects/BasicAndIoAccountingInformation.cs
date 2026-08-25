// <copyright file="BasicAndIoAccountingInformation.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.JobObjects
{
    using System.Runtime.InteropServices;

    /// <summary>
    /// JOBOBJECT_BASIC_AND_IO_ACCOUNTING_INFORMATION.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BasicAndIoAccountingInformation
    {
        public BasicAccountingInformation BasicInfo;
        public IoCounters IoInfo;
    }
}
