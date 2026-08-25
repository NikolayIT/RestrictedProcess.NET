// <copyright file="RestrictedProcessExecutor.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Abstractions;

    using RestrictedProcess.Process;

    public class RestrictedProcessExecutor : IExecutor
    {
        private readonly ILogger logger;
        private readonly RestrictedProcessOptions options;

        public RestrictedProcessExecutor(ILogger? logger = null)
            : this(new RestrictedProcessOptions(), logger)
        {
        }

        public RestrictedProcessExecutor(RestrictedProcessOptions options, ILogger? logger = null)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.logger = logger ?? NullLogger.Instance;
        }

        // TODO: double check and maybe change order of parameters
        public ProcessExecutionResult Execute(string fileName, string inputData, int timeLimit, int memoryLimit, IEnumerable<string>? executionArguments = null)
        {
            var result = new ProcessExecutionResult { Type = ProcessExecutionResultType.Success };
            var workingDirectory = this.options.WorkingDirectory ?? new FileInfo(fileName).DirectoryName;
            var outputLimitReached = false;

            using (var restrictedProcess = new RestrictedProcess(fileName, workingDirectory, executionArguments, Math.Max(4096, (inputData.Length * 2) + 4), this.options.Encoding, this.options))
            {
                // Write to standard input using another thread
                restrictedProcess.StandardInput.WriteLineAsync(inputData).ContinueWith(
                    delegate
                    {
                        // ReSharper disable once AccessToDisposedClosure
                        if (!restrictedProcess.IsDisposed)
                        {
                            // ReSharper disable once AccessToDisposedClosure
                            restrictedProcess.StandardInput.FlushAsync().ContinueWith(
                                delegate
                                {
                                    restrictedProcess.StandardInput.Close();
                                });
                        }
                    });

                // Read standard output using another thread to prevent process locking (waiting us to empty the output buffer).
                // Reading is bounded so a program that floods its output cannot exhaust the host's memory.
                var processOutputTask = ReadBoundedAsync(restrictedProcess.StandardOutput, this.options.MaxOutputSize)
                    .ContinueWith(
                        x =>
                        {
                            result.ReceivedOutput = x.Result.Text;
                            if (x.Result.Truncated)
                            {
                                Volatile.Write(ref outputLimitReached, true);
                                restrictedProcess.Kill();
                            }
                        });

                // Read standard error using another thread
                var errorOutputTask = ReadBoundedAsync(restrictedProcess.StandardError, this.options.MaxErrorSize)
                    .ContinueWith(
                        x =>
                        {
                            result.ErrorOutput = x.Result.Text;
                            if (x.Result.Truncated)
                            {
                                Volatile.Write(ref outputLimitReached, true);
                                restrictedProcess.Kill();
                            }
                        });

                // Read memory consumption every few milliseconds to determine the peak memory usage of the process
                const int TimeIntervalBetweenTwoMemoryConsumptionRequests = 45;
                var memoryTaskCancellationToken = new CancellationTokenSource();
                var memoryTask = Task.Run(
                    () =>
                    {
                        while (true)
                        {
                            // ReSharper disable once AccessToDisposedClosure
                            var peakWorkingSetSize = restrictedProcess.PeakWorkingSetSize;

                            result.MemoryUsed = Math.Max(result.MemoryUsed, peakWorkingSetSize);

                            if (memoryTaskCancellationToken.IsCancellationRequested)
                            {
                                return;
                            }

                            Thread.Sleep(TimeIntervalBetweenTwoMemoryConsumptionRequests);
                        }
                    },
                    memoryTaskCancellationToken.Token);

                // Start the process
                restrictedProcess.Start(timeLimit, memoryLimit);

                // Wait the process to complete. Kill it after (timeLimit * WallClockWaitMultiplier) milliseconds if not completed.
                // We are waiting the process for more than defined time and after this we compare the process time with the real time limit.
                var exited = restrictedProcess.WaitForExit((int)(timeLimit * Math.Max(1.0, this.options.WallClockWaitMultiplier)));
                if (!exited)
                {
                    restrictedProcess.Kill();
                    result.Type = ProcessExecutionResultType.TimeLimit;
                }

                // Close the memory consumption check thread
                memoryTaskCancellationToken.Cancel();
                try
                {
                    // To be sure that memory consumption will be evaluated correctly
                    memoryTask.Wait(TimeIntervalBetweenTwoMemoryConsumptionRequests);
                }
                catch (AggregateException ex)
                {
                    this.logger.LogWarning(ex.InnerException, "AggregateException caught.");
                }

                // Close the task that gets the process error output
                try
                {
                    errorOutputTask.Wait(100);
                }
                catch (AggregateException ex)
                {
                    this.logger.LogWarning(ex.InnerException, "AggregateException caught.");
                }

                // Close the task that gets the process output
                try
                {
                    processOutputTask.Wait(100);
                }
                catch (AggregateException ex)
                {
                    this.logger.LogWarning(ex.InnerException, "AggregateException caught.");
                }

                Debug.Assert(restrictedProcess.HasExited, "Restricted process didn't exit!");

                // The job object keeps track of the peak memory committed by the process even after it has
                // exited, so use it in addition to the sampled working set (short-lived processes can consume
                // and release memory between two samples).
                result.MemoryUsed = Math.Max(result.MemoryUsed, restrictedProcess.PeakJobMemoryUsed);

                // Report exit code and total process working time
                result.ExitCode = restrictedProcess.ExitCode;
                result.TimeWorked = restrictedProcess.ExitTime - restrictedProcess.StartTime;
                result.PrivilegedProcessorTime = restrictedProcess.PrivilegedProcessorTime;
                result.UserProcessorTime = restrictedProcess.UserProcessorTime;
            }

            if (result.TotalProcessorTime.TotalMilliseconds > timeLimit)
            {
                result.Type = ProcessExecutionResultType.TimeLimit;
            }

            if (!string.IsNullOrEmpty(result.ErrorOutput))
            {
                result.Type = ProcessExecutionResultType.RunTimeError;
            }

            if (result.MemoryUsed > memoryLimit)
            {
                result.Type = ProcessExecutionResultType.MemoryLimit;
            }

            // A non-zero exit code with no other limit tripped and no error output is still a failed run.
            if (this.options.TreatNonZeroExitCodeAsRunTimeError
                && result.ExitCode != 0
                && result.Type == ProcessExecutionResultType.Success)
            {
                result.Type = ProcessExecutionResultType.RunTimeError;
            }

            // Output flooding takes precedence over a runtime error from the (truncated) error output.
            if (outputLimitReached
                && (result.Type == ProcessExecutionResultType.Success || result.Type == ProcessExecutionResultType.RunTimeError))
            {
                result.Type = ProcessExecutionResultType.OutputLimit;
            }

            return result;
        }

        private static async Task<(string Text, bool Truncated)> ReadBoundedAsync(StreamReader reader, long maxCharacters)
        {
            if (maxCharacters <= 0)
            {
                return (await reader.ReadToEndAsync().ConfigureAwait(false), false);
            }

            var builder = new StringBuilder();
            var buffer = new char[4096];
            int read;
            while ((read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
            {
                var remaining = maxCharacters - builder.Length;
                if (read >= remaining)
                {
                    builder.Append(buffer, 0, (int)remaining);
                    return (builder.ToString(), true);
                }

                builder.Append(buffer, 0, read);
            }

            return (builder.ToString(), false);
        }
    }
}
