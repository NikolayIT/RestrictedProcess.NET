// <copyright file="ExecutionResultClassifier.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess
{
    /// <summary>
    /// Turns the measurements of a finished run into a single verdict.
    /// <para>
    /// A judge shows one outcome per submission, so when a run trips several conditions at once the order
    /// is part of the contract. It lives here, once, because the sandboxed and the plain executor have to
    /// agree: while each had its own copy they had already drifted, and a program that exceeded its
    /// processor time limit was reported as a runtime error purely because it had printed a warning.
    /// </para>
    /// <para>
    /// Highest to lowest: a resource limit the run was stopped for beats everything, because it is the
    /// reason the program did not finish; among those, memory beats time. Below them, output flooding
    /// beats a plain runtime error, and a runtime error beats success.
    /// </para>
    /// </summary>
    internal static class ExecutionResultClassifier
    {
        /// <summary>
        /// Assigns <see cref="ProcessExecutionResult.Type"/> from the figures already on the result.
        /// </summary>
        /// <param name="result">The result to classify, with its measurements filled in.</param>
        /// <param name="request">The limits the run was judged against.</param>
        /// <param name="options">The options the run used.</param>
        /// <param name="deadlineReached">Whether the wall-clock deadline stopped the run.</param>
        /// <param name="cancelled">Whether the caller cancelled the run.</param>
        /// <param name="diskWriteExceeded">Whether the job reported the disk write limit as exceeded.</param>
        public static void Classify(
            ProcessExecutionResult result,
            ExecutionRequest request,
            RestrictedProcessOptions options,
            bool deadlineReached,
            bool cancelled,
            bool diskWriteExceeded)
        {
            if (cancelled)
            {
                result.Type = ProcessExecutionResultType.Cancelled;
                return;
            }

            if (deadlineReached
                || (request.CpuTimeLimit.HasValue && result.TotalProcessorTime > request.CpuTimeLimit.Value))
            {
                result.Type = ProcessExecutionResultType.TimeLimit;
            }

            if (request.MemoryLimitBytes.HasValue && result.MemoryUsed > request.MemoryLimitBytes.Value)
            {
                result.Type = ProcessExecutionResultType.MemoryLimit;
            }

            // Standard error and a non-zero exit code only decide the verdict when no resource limit was
            // hit. A program stopped at its limit has usually written something on the way out, and that
            // must not hide why it was stopped.
            if (result.Type == ProcessExecutionResultType.Success && !string.IsNullOrEmpty(result.ErrorOutput))
            {
                result.Type = ProcessExecutionResultType.RunTimeError;
            }

            if (options.TreatNonZeroExitCodeAsRunTimeError
                && result.ExitCode != 0
                && result.Type == ProcessExecutionResultType.Success)
            {
                result.Type = ProcessExecutionResultType.RunTimeError;
            }

            // OutputLimit covers a program that produced too much, whether down a pipe or onto disk.
            var wroteTooMuch = result.OutputTruncated
                               || result.ErrorTruncated
                               || diskWriteExceeded
                               || (options.MaxDiskWriteBytes.HasValue
                                   && result.IoStatistics.WriteBytes > (ulong)options.MaxDiskWriteBytes.Value);

            if (wroteTooMuch
                && (result.Type == ProcessExecutionResultType.Success
                    || result.Type == ProcessExecutionResultType.RunTimeError))
            {
                result.Type = ProcessExecutionResultType.OutputLimit;
            }
        }
    }
}
