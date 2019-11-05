// <copyright file="ProcessExecutionResult.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

using System;

namespace RestrictedProcessCore
{
    public class ProcessExecutionResult
    {
        public ProcessExecutionResult()
        {
            this.ReceivedOutput = string.Empty;
            this.ErrorOutput = string.Empty;
            this.ExitCode = 0;
            this.Type = ProcessExecutionResultType.Success;
            this.TimeWorked = default(TimeSpan);
            this.MemoryUsed = 0;
        }

        public string ReceivedOutput { get; set; }

        public string ErrorOutput { get; set; }

        public int ExitCode { get; set; }

        public ProcessExecutionResultType Type { get; set; }

        public TimeSpan TimeWorked { get; set; }

        public long MemoryUsed { get; set; }

        public TimeSpan PrivilegedProcessorTime { get; set; }

        public TimeSpan UserProcessorTime { get; set; }

        public TimeSpan TotalProcessorTime => this.PrivilegedProcessorTime + this.UserProcessorTime;
    }
}
