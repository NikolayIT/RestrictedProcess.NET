// <copyright file="PrepareJobObject.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.JobObjects
{
    using System;

    internal static class PrepareJobObject
    {
        public static ExtendedLimitInformation GetExtendedLimitInformation(RestrictedProcessOptions options, int maximumMemory)
        {
            // Only the job-wide memory limit is used (not JOB_OBJECT_LIMIT_PROCESS_MEMORY): with the
            // 2x backstop the job limit lets a program allocate past the requested limit so the
            // executor can measure the overage and classify it as a memory-limit result. A per-process
            // commit limit instead fails the allocation atomically, leaving committed memory unchanged
            // and unmeasurable, which would misclassify an over-limit program as a runtime error.
            var limitFlags = LimitFlags.JOB_OBJECT_LIMIT_JOB_MEMORY
                             //// The following two flags are causing the process to have unexpected behavior
                             //// | LimitFlags.JOB_OBJECT_LIMIT_JOB_TIME
                             //// | LimitFlags.JOB_OBJECT_LIMIT_PROCESS_TIME
                             | LimitFlags.JOB_OBJECT_LIMIT_ACTIVE_PROCESS
                             | LimitFlags.JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION
                             | LimitFlags.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

            var info = new BasicLimitInformation
            {
                ActiveProcessLimit = Math.Max(1, options.ActiveProcessLimit),
            };

            if (options.ProcessorAffinityMask.HasValue)
            {
                limitFlags |= LimitFlags.JOB_OBJECT_LIMIT_AFFINITY;
                info.Affinity = options.ProcessorAffinityMask.Value;
            }

            info.LimitFlags = (uint)limitFlags;

            var extendedInfo = new ExtendedLimitInformation
            {
                BasicLimitInformation = info,
                JobMemoryLimit = (UIntPtr)maximumMemory,
                IoInfo =
                {
                    ReadTransferCount = 0,
                    ReadOperationCount = 0,
                    WriteOperationCount = 0,
                    WriteTransferCount = 0,
                },
            };

            return extendedInfo;
        }

        public static BasicUiRestrictions GetUiRestrictions()
        {
            var restrictions = new BasicUiRestrictions
                                   {
                                       UIRestrictionsClass =
                                           (int)(UiRestrictionFlags.JOB_OBJECT_UILIMIT_DESKTOP
                                            | UiRestrictionFlags.JOB_OBJECT_UILIMIT_DISPLAYSETTINGS
                                            | UiRestrictionFlags.JOB_OBJECT_UILIMIT_EXITWINDOWS
                                            | UiRestrictionFlags.JOB_OBJECT_UILIMIT_GLOBALATOMS
                                            | UiRestrictionFlags.JOB_OBJECT_UILIMIT_HANDLES
                                            | UiRestrictionFlags.JOB_OBJECT_UILIMIT_READCLIPBOARD
                                            | UiRestrictionFlags.JOB_OBJECT_UILIMIT_SYSTEMPARAMETERS
                                            | UiRestrictionFlags.JOB_OBJECT_UILIMIT_WRITECLIPBOARD),
                                   };

            return restrictions;
        }
    }
}
