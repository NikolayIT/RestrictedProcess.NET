// <copyright file="ProcessThreadTimes.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

using System;

namespace RestrictedProcessCore.Process
{
    internal struct ProcessThreadTimes
    {
        public long Create;
        public long Exit;
        public long Kernel;
        public long User;

        public DateTime StartTime => DateTime.FromFileTime(this.Create);

        public DateTime ExitTime => DateTime.FromFileTime(this.Exit);

        public TimeSpan PrivilegedProcessorTime => new TimeSpan(this.Kernel);

        public TimeSpan UserProcessorTime => new TimeSpan(this.User);

        public TimeSpan TotalProcessorTime => new TimeSpan(this.User + this.Kernel);
    }
}
