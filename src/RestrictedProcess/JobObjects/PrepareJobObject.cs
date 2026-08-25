// <copyright file="PrepareJobObject.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.JobObjects
{
    using System;

    internal static class PrepareJobObject
    {
        /// <summary>
        /// Builds the hard, OS-enforced limits for the job.
        /// </summary>
        /// <param name="options">The sandbox options.</param>
        /// <param name="maximumMemoryBytes">
        /// The hard job-wide committed memory backstop in bytes, or zero for no backstop.
        /// </param>
        /// <returns>The extended limit information to apply to the job.</returns>
        public static ExtendedLimitInformation GetExtendedLimitInformation(
            RestrictedProcessOptions options, long maximumMemoryBytes)
        {
            // Only the job-wide memory limit is used (not JOB_OBJECT_LIMIT_PROCESS_MEMORY): with the
            // JobLimitsMultiplier backstop the job limit lets a program allocate past the requested limit
            // so the overage stays measurable, and the exact breach is reported separately through the
            // notification limits below. A per-process commit limit instead fails the allocation
            // atomically, leaving committed memory unchanged and unmeasurable, which would misclassify an
            // over-limit program as a runtime error.
            var limitFlags = LimitFlags.JOB_OBJECT_LIMIT_ACTIVE_PROCESS
                             | LimitFlags.JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION
                             | LimitFlags.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

            // The hard job time limits stay off: JOB_OBJECT_LIMIT_JOB_TIME terminates the whole job the
            // moment it trips, which loses the measurement. The same threshold is applied as a
            // notification limit instead, which reports the breach without killing anything.
            if (maximumMemoryBytes > 0)
            {
                limitFlags |= LimitFlags.JOB_OBJECT_LIMIT_JOB_MEMORY;
            }

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

            return new ExtendedLimitInformation
            {
                BasicLimitInformation = info,
                JobMemoryLimit = maximumMemoryBytes > 0 ? (UIntPtr)(ulong)maximumMemoryBytes : UIntPtr.Zero,
            };
        }

        /// <summary>
        /// Builds the soft notification limits. Exceeding one of these posts
        /// JOB_OBJECT_MSG_NOTIFICATION_LIMIT to the completion port rather than failing an allocation or
        /// terminating the job, so the breach is detected immediately while the run stays measurable.
        /// </summary>
        /// <param name="memoryLimitBytes">The committed memory threshold, or null for none.</param>
        /// <param name="cpuTimeLimit">The job-wide user-mode processor time threshold, or null for none.</param>
        /// <param name="writeBytesLimit">The job-wide disk write threshold in bytes, or null for none.</param>
        /// <returns>The notification limits, or null when nothing needs to be watched.</returns>
        public static NotificationLimitInformation? GetNotificationLimits(
            long? memoryLimitBytes, TimeSpan? cpuTimeLimit, long? writeBytesLimit)
        {
            var limitFlags = default(LimitFlags);
            var notification = default(NotificationLimitInformation);

            if (memoryLimitBytes.HasValue && memoryLimitBytes.Value > 0)
            {
                limitFlags |= LimitFlags.JOB_OBJECT_LIMIT_JOB_MEMORY;
                notification.JobMemoryLimit = (ulong)memoryLimitBytes.Value;
            }

            if (cpuTimeLimit.HasValue && cpuTimeLimit.Value > TimeSpan.Zero)
            {
                limitFlags |= LimitFlags.JOB_OBJECT_LIMIT_JOB_TIME;
                notification.PerJobUserTimeLimit = cpuTimeLimit.Value.Ticks;
            }

            if (writeBytesLimit.HasValue && writeBytesLimit.Value > 0)
            {
                limitFlags |= LimitFlags.JOB_OBJECT_LIMIT_JOB_WRITE_BYTES;
                notification.IoWriteBytesLimit = (ulong)writeBytesLimit.Value;
            }

            if (limitFlags == default(LimitFlags))
            {
                return null;
            }

            notification.LimitFlags = (uint)limitFlags;
            return notification;
        }

        public static BasicUiRestrictions GetUiRestrictions()
        {
            return new BasicUiRestrictions
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
        }
    }
}
