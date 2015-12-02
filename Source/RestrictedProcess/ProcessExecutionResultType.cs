// <copyright file="ProcessExecutionResultType.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess
{
    public enum ProcessExecutionResultType
    {
        Success = 0,
        TimeLimit = 1,
        MemoryLimit = 2,
        RunTimeError = 4,
    }
}
