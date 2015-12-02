// <copyright file="SecurityLimitFlags.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.JobObjects
{
    public enum SecurityLimitFlags
    {
        /// <summary>
        /// Prevents any process in the job from using a token that specifies the local administrators group.
        /// </summary>
        JOB_OBJECT_SECURITY_NO_ADMIN = 0x00000001,

        /// <summary>
        /// Prevents any process in the job from using a token that was not created with the CreateRestrictedToken function.
        /// </summary>
        JOB_OBJECT_SECURITY_RESTRICTED_TOKEN = 0x00000002,

        /// <summary>
        /// Forces processes in the job to run under a specific token. Requires a token handle in the JobToken member.
        /// </summary>
        JOB_OBJECT_SECURITY_ONLY_TOKEN = 0x00000004,

        /// <summary>
        /// Applies a filter to the token when a process impersonates a client. Requires at least one of the following members to be set: SidsToDisable, PrivilegesToDelete, or RestrictedSids.
        /// </summary>
        JOB_OBJECT_SECURITY_FILTER_TOKENS = 0x00000008,
    }
}
