// <copyright file="IExecutor.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

using System.Collections.Generic;

namespace RestrictedProcessCore
{
    public interface IExecutor
    {
        ProcessExecutionResult Execute(string fileName, string inputData, int timeLimit, int memoryLimit, IEnumerable<string> executionArguments = null);
    }
}
