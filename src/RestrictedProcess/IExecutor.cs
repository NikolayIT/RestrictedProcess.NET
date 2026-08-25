// <copyright file="IExecutor.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Runs a program and reports what it produced and what it cost.
    /// </summary>
    public interface IExecutor
    {
        /// <summary>
        /// Runs the program described by the request.
        /// </summary>
        /// <param name="request">What to run and the limits to enforce.</param>
        /// <param name="cancellationToken">Cancelling kills the process and yields a
        /// <see cref="ProcessExecutionResultType.Cancelled"/> result.</param>
        /// <returns>The outcome of the run.</returns>
        Task<ProcessExecutionResult> ExecuteAsync(ExecutionRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Runs the program described by the request, blocking until it finishes. Prefer
        /// <see cref="ExecuteAsync"/>; this overload exists for synchronous callers.
        /// </summary>
        /// <param name="request">What to run and the limits to enforce.</param>
        /// <returns>The outcome of the run.</returns>
        ProcessExecutionResult Execute(ExecutionRequest request);
    }
}
