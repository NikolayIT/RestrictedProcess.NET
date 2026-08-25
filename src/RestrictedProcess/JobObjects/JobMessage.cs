// <copyright file="JobMessage.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.JobObjects
{
    /// <summary>
    /// The JOB_OBJECT_MSG_* completion keys posted to the completion port of a job object.
    /// </summary>
    internal enum JobMessage
    {
        EndOfJobTime = 1,
        EndOfProcessTime = 2,
        ActiveProcessLimit = 3,
        ActiveProcessZero = 4,
        NewProcess = 6,
        ExitProcess = 7,
        AbnormalExitProcess = 8,
        ProcessMemoryLimit = 9,
        JobMemoryLimit = 10,
        NotificationLimit = 11,
        JobCycleTimeLimit = 12,
        SiloTerminated = 13,
    }
}
