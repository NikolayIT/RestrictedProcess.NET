// <copyright file="InfoClass.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcessCore.JobObjects
{
    /// <summary>
    /// Job object information type.
    /// </summary>
    internal enum InfoClass
    {
        /// <summary>
        /// The basic accounting information.
        /// </summary>
        BasicAccountingInformation = 1,

        /// <summary>
        /// The basic limit information.
        /// </summary>
        BasicLimitInformation = 2,

        /// <summary>
        /// The basic process id list.
        /// </summary>
        BasicProcessIdList = 3,

        /// <summary>
        /// The basic UI restrictions.
        /// </summary>
        BasicUiRestrictions = 4,

        /// <summary>
        /// The security limit information (deprecated).
        /// </summary>
        SecurityLimitInformation = 5,

        /// <summary>
        /// The end of job time information.
        /// </summary>
        EndOfJobTimeInformation = 6,

        /// <summary>
        /// The associate completion port information.
        /// </summary>
        AssociateCompletionPortInformation = 7,

        /// <summary>
        /// The basic and io accounting information.
        /// </summary>
        BasicAndIoAccountingInformation = 8,

        /// <summary>
        /// The extended limit information.
        /// </summary>
        ExtendedLimitInformation = 9,

        /// <summary>
        /// The job set information.
        /// </summary>
        JobSetInformation = 10,

        /// <summary>
        /// The group information.
        /// </summary>
        GroupInformation = 11,

        /// <summary>
        /// The notification limit information.
        /// </summary>
        NotificationLimitInformation = 12,

        /// <summary>
        /// The limit violation information.
        /// </summary>
        LimitViolationInformation = 13,

        /// <summary>
        /// The group information ex.
        /// </summary>
        GroupInformationEx = 14,

        /// <summary>
        /// The CPU rate control information.
        /// </summary>
        CpuRateControlInformation = 15,

        /// <summary>
        /// The completion filter.
        /// </summary>
        CompletionFilter = 16,

        /// <summary>
        /// The completion counter.
        /// </summary>
        CompletionCounter = 17,

        /// <summary>
        /// The reserved 1 information.
        /// </summary>
        Reserved1Information = 18,

        /// <summary>
        /// The reserved 2 information.
        /// </summary>
        Reserved2Information = 19,

        /// <summary>
        /// The reserved 3 information.
        /// </summary>
        Reserved3Information = 20,

        /// <summary>
        /// The reserved 4 information.
        /// </summary>
        Reserved4Information = 21,

        /// <summary>
        /// The reserved 5 information.
        /// </summary>
        Reserved5Information = 22,

        /// <summary>
        /// The reserved 6 information.
        /// </summary>
        Reserved6Information = 23,

        /// <summary>
        /// The reserved 7 information.
        /// </summary>
        Reserved7Information = 24,

        /// <summary>
        /// The reserved 8 information.
        /// </summary>
        Reserved8Information = 25,

        /// <summary>
        /// The reserved 9 information.
        /// </summary>
        Reserved9Information = 26,

        /// <summary>
        /// The max job object info class.
        /// </summary>
        MaxJobObjectInfoClass = 27,
    }
}
