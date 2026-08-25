// <copyright file="RateControlToleranceInterval.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.JobObjects
{
    /// <summary>
    /// JOBOBJECT_RATE_CONTROL_TOLERANCE_INTERVAL.
    /// </summary>
    internal enum RateControlToleranceInterval
    {
        None = 0,
        Short = 1,
        Medium = 2,
        Long = 3,
    }
}
