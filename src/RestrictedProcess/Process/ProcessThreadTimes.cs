// <copyright file="ProcessThreadTimes.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.Process
{
    using System;

    /// <summary>
    /// The four raw FILETIME values returned by GetProcessTimes, kept as ticks. Converting them through
    /// <see cref="DateTime.FromFileTime(long)"/> would move them into local time, which makes an elapsed
    /// time computed from the difference wrong across a daylight saving transition.
    /// </summary>
    internal struct ProcessThreadTimes
    {
        public long Create;
        public long Exit;
        public long Kernel;
        public long User;

        public TimeSpan PrivilegedProcessorTime => new TimeSpan(this.Kernel);

        public TimeSpan UserProcessorTime => new TimeSpan(this.User);

        public TimeSpan TotalProcessorTime => new TimeSpan(this.User + this.Kernel);
    }
}
