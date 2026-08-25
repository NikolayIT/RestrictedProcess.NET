// <copyright file="RateControlTolerance.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.JobObjects
{
    /// <summary>
    /// JOBOBJECT_RATE_CONTROL_TOLERANCE.
    /// </summary>
    internal enum RateControlTolerance
    {
        None = 0,
        Low = 1,
        Medium = 2,
        High = 3,
    }
}
