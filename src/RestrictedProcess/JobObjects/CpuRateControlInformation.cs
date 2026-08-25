// <copyright file="CpuRateControlInformation.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.JobObjects
{
    using System.Runtime.InteropServices;

    /// <summary>
    /// Contains CPU rate control information for a job object
    /// (JOBOBJECT_CPU_RATE_CONTROL_INFORMATION).
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    internal struct CpuRateControlInformation
    {
        /// <summary>
        /// Enables the CPU rate control (JOB_OBJECT_CPU_RATE_CONTROL_ENABLE).
        /// </summary>
        public const uint FlagEnable = 0x1;

        /// <summary>
        /// The portion specified by <see cref="CpuRate"/> is a hard cap
        /// (JOB_OBJECT_CPU_RATE_CONTROL_HARD_CAP).
        /// </summary>
        public const uint FlagHardCap = 0x4;

        [FieldOffset(0)]
        public uint ControlFlags;

        /// <summary>
        /// The portion of processor cycles the job can use, in units of 1/100 of a percent
        /// (so 25% is 2500). Used when the weight-based control flag is not set.
        /// </summary>
        [FieldOffset(4)]
        public uint CpuRate;

        /// <summary>
        /// The scheduling weight of the job (1-9). Overlaps <see cref="CpuRate"/> in the native union.
        /// </summary>
        [FieldOffset(4)]
        public uint Weight;
    }
}
