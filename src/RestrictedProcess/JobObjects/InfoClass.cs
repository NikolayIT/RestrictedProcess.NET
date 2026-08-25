// <copyright file="InfoClass.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.JobObjects
{
    /// <summary>
    /// JOBOBJECTINFOCLASS. Only the classes this library uses are named; the values match winnt.h.
    /// </summary>
    internal enum InfoClass
    {
        BasicAccountingInformation = 1,
        BasicLimitInformation = 2,
        BasicProcessIdList = 3,
        BasicUiRestrictions = 4,
        SecurityLimitInformation = 5,
        EndOfJobTimeInformation = 6,
        AssociateCompletionPortInformation = 7,
        BasicAndIoAccountingInformation = 8,
        ExtendedLimitInformation = 9,
        JobSetInformation = 10,
        GroupInformation = 11,
        NotificationLimitInformation = 12,
        LimitViolationInformation = 13,
        GroupInformationEx = 14,
        CpuRateControlInformation = 15,
        CompletionFilter = 16,
        CompletionCounter = 17,
        NetRateControlInformation = 32,
        NotificationLimitInformation2 = 33,
        LimitViolationInformation2 = 34,
        IoRateControlInformation = 46,
    }
}
