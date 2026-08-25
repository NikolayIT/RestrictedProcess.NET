// <copyright file="RestrictedProcessExecutor.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Abstractions;

    using RestrictedProcess.Process;

    /// <summary>
    /// Runs a program inside the sandbox and enforces its limits.
    /// <para>
    /// Limits are enforced in two tiers. The job object gets a loosened backstop
    /// (<see cref="RestrictedProcessOptions.JobLimitsMultiplier"/> times the requested memory) so the OS
    /// stops a runaway program without ever letting it take the machine down, while the exact limits are
    /// applied as job <em>notification</em> limits: the program is allowed to cross them, which is what
    /// keeps the overage measurable, and the job reports the breach immediately so the run can be stopped
    /// without waiting out the clock.
    /// </para>
    /// </summary>
    public sealed class RestrictedProcessExecutor : IExecutor
    {
        private const int ReadBufferSize = 8192;

        private readonly ILogger logger;
        private readonly RestrictedProcessOptions options;

        /// <summary>
        /// Initializes a new instance of the <see cref="RestrictedProcessExecutor"/> class with the default
        /// sandbox options.
        /// </summary>
        /// <param name="logger">An optional logger for diagnostics.</param>
        public RestrictedProcessExecutor(ILogger? logger = null)
            : this(new RestrictedProcessOptions(), logger)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RestrictedProcessExecutor"/> class.
        /// </summary>
        /// <param name="options">The sandbox configuration.</param>
        /// <param name="logger">An optional logger for diagnostics.</param>
        public RestrictedProcessExecutor(RestrictedProcessOptions options, ILogger? logger = null)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.logger = logger ?? NullLogger.Instance;
        }

        private enum WaitResult
        {
            Exited = 0,
            TimedOut = -1,
            Cancelled = -2,
        }

        /// <inheritdoc/>
        public ProcessExecutionResult Execute(ExecutionRequest request)
        {
            // Safe to block on: every await below is configured off the captured context.
            return this.ExecuteAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public async Task<ProcessExecutionResult> ExecuteAsync(ExecutionRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var result = new ProcessExecutionResult();
            var startInfo = this.BuildStartInfo(request);
            var wallClockLimit = this.ResolveWallClockLimit(request);

            var outputTruncated = false;
            var errorTruncated = false;
            var cancelled = false;
            var deadlineReached = false;
            var diskWriteExceeded = false;

            using (var process = new RestrictedProcess(startInfo, this.options))
            {
                var inputTask = WriteInputAsync(process, request.Input);
                var outputTask = ReadBoundedAsync(
                    process.StandardOutput, this.options.MaxOutputSize, () => process.Kill());
                var errorTask = ReadBoundedAsync(
                    process.StandardError, this.options.MaxErrorSize, () => process.Kill());

                process.Start();

                // Wait for whichever comes first: the process exiting, the job reporting that a limit was
                // crossed, the wall-clock deadline, or the caller cancelling.
                var signalled = await WaitAnyAsync(
                    new[] { process.ExitedHandle, process.NotificationLimitReached },
                    wallClockLimit,
                    cancellationToken).ConfigureAwait(false);

                switch (signalled)
                {
                    case (int)WaitResult.Cancelled:
                        cancelled = true;
                        process.Kill();
                        break;

                    case (int)WaitResult.TimedOut:
                        deadlineReached = true;
                        process.Kill();
                        break;

                    case 1:
                        // A soft limit was crossed. Which one is worked out from the measured figures
                        // below, except for the disk write limit, which leaves no trace in them - ask the
                        // job before killing the process.
                        diskWriteExceeded = process.DiskWriteLimitExceeded;
                        process.Kill();
                        break;
                }

                // The peak working set is only observable while the process is alive, so take a reading as
                // close to the end as possible. Committed memory comes from the job and survives the exit.
                process.SampleWorkingSet();

                if (signalled != (int)WaitResult.Exited)
                {
                    await WaitAnyAsync(new[] { process.ExitedHandle }, this.options.OutputDrainTimeout, CancellationToken.None)
                        .ConfigureAwait(false);
                }

                // Every write handle for the pipes is gone once the process is, so the readers reach end of
                // file on their own. The timeout is a safety net, not the normal path - unlike the fixed
                // 100 ms wait this used to use, which quietly dropped large outputs.
                await this.DrainAsync(inputTask, outputTask, errorTask).ConfigureAwait(false);

                var output = ResultOf(outputTask);
                var error = ResultOf(errorTask);
                result.ReceivedOutput = output.Text;
                result.ErrorOutput = error.Text;
                outputTruncated = output.Truncated;
                errorTruncated = error.Truncated;

                result.ExitCode = process.ExitCode;
                result.TimeWorked = process.WallClockTime;
                result.UserProcessorTime = process.UserProcessorTime;
                result.PrivilegedProcessorTime = process.PrivilegedProcessorTime;
                result.PeakCommitBytes = process.PeakCommitBytes;
                result.PeakWorkingSetBytes = process.PeakWorkingSetBytes;
                result.IoStatistics = process.IoStatistics;
            }

            result.OutputTruncated = outputTruncated;
            result.ErrorTruncated = errorTruncated;
            result.MemoryUsed = this.SelectMemoryMetric(result);

            ExecutionResultClassifier.Classify(
                result, request, this.options, deadlineReached, cancelled, diskWriteExceeded);
            return result;
        }

        private static (string Text, bool Truncated) ResultOf(Task<(string Text, bool Truncated)> task)
        {
            return task.Status == TaskStatus.RanToCompletion ? task.Result : (string.Empty, false);
        }

        /// <summary>
        /// Reads a stream up to a cap. Once the cap is reached the reader stops storing but keeps draining
        /// to end of file, so a program that is still writing never blocks on a full pipe while it is being
        /// killed.
        /// </summary>
        private static async Task<(string Text, bool Truncated)> ReadBoundedAsync(
            StreamReader reader, long maxCharacters, Action onTruncated)
        {
            var builder = new StringBuilder();
            var buffer = new char[ReadBufferSize];
            var truncated = false;

            try
            {
                int read;
                while ((read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                {
                    if (truncated)
                    {
                        continue;
                    }

                    if (maxCharacters > 0 && builder.Length + read > maxCharacters)
                    {
                        builder.Append(buffer, 0, (int)(maxCharacters - builder.Length));
                        truncated = true;
                        onTruncated();
                        continue;
                    }

                    builder.Append(buffer, 0, read);
                }
            }
            catch (IOException)
            {
                // The pipe was torn down under us by a kill; whatever was read so far still counts.
            }
            catch (ObjectDisposedException)
            {
            }

            return (builder.ToString(), truncated);
        }

        private static async Task WriteInputAsync(RestrictedProcess process, string input)
        {
            try
            {
                await process.StandardInput.WriteLineAsync(input ?? string.Empty).ConfigureAwait(false);
                await process.StandardInput.FlushAsync().ConfigureAwait(false);
            }
            catch (IOException)
            {
                // A program is free to ignore its standard input and exit; the write then fails, which is
                // not an error of the run.
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                try
                {
                    // Closing the write end is what lets a program that reads to end of input finish.
                    process.StandardInput.Close();
                }
                catch (IOException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        /// <summary>
        /// Waits for the first of several kernel objects to be signalled without blocking a thread, using
        /// the thread pool's wait infrastructure rather than a dedicated waiter.
        /// </summary>
        /// <returns>
        /// The index of the handle that was signalled, <see cref="WaitResult.TimedOut"/> or
        /// <see cref="WaitResult.Cancelled"/>.
        /// </returns>
        private static Task<int> WaitAnyAsync(WaitHandle[] handles, TimeSpan? timeout, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var registrations = new RegisteredWaitHandle[handles.Length];
            var cancellationRegistration = default(CancellationTokenRegistration);
            var settled = 0;

            void Complete(int outcome)
            {
                if (Interlocked.Exchange(ref settled, 1) == 0)
                {
                    completion.TrySetResult(outcome);
                }
            }

            for (var i = 0; i < handles.Length; i++)
            {
                var index = i;
                registrations[i] = ThreadPool.RegisterWaitForSingleObject(
                    handles[i],
                    (_, timedOut) => Complete(timedOut ? (int)WaitResult.TimedOut : index),
                    null,
                    timeout ?? Timeout.InfiniteTimeSpan,
                    true);
            }

            if (cancellationToken.CanBeCanceled)
            {
                cancellationRegistration = cancellationToken.Register(() => Complete((int)WaitResult.Cancelled));
            }

            return completion.Task.ContinueWith(
                task =>
                {
                    foreach (var registration in registrations)
                    {
                        registration?.Unregister(null);
                    }

                    cancellationRegistration.Dispose();
                    return task.Result;
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private RestrictedProcessStartInfo BuildStartInfo(ExecutionRequest request)
        {
            var workingDirectory = request.WorkingDirectory
                                   ?? this.options.WorkingDirectory
                                   ?? Path.GetDirectoryName(Path.GetFullPath(request.FileName));

            return new RestrictedProcessStartInfo(request.FileName)
            {
                Arguments = request.Arguments,
                WorkingDirectory = workingDirectory,
                MemoryLimitBytes = request.MemoryLimitBytes,
                CpuTimeLimit = request.CpuTimeLimit,
                Encoding = this.options.Encoding,
            };
        }

        private TimeSpan? ResolveWallClockLimit(ExecutionRequest request)
        {
            if (request.WallClockLimit.HasValue)
            {
                return request.WallClockLimit;
            }

            if (!request.CpuTimeLimit.HasValue)
            {
                return null;
            }

            var multiplier = Math.Max(1.0, this.options.WallClockWaitMultiplier);
            return TimeSpan.FromMilliseconds(request.CpuTimeLimit.Value.TotalMilliseconds * multiplier);
        }

        private async Task DrainAsync(Task inputTask, Task outputTask, Task errorTask)
        {
            var readers = Task.WhenAll(outputTask, errorTask, inputTask);
            var finished = await Task.WhenAny(readers, Task.Delay(this.options.OutputDrainTimeout)).ConfigureAwait(false);

            if (finished != readers)
            {
                this.logger.LogWarning(
                    "The standard IO of the sandboxed process did not reach end of file within {Timeout}; the captured output may be incomplete.",
                    this.options.OutputDrainTimeout);
            }
        }

        private long SelectMemoryMetric(ProcessExecutionResult result)
        {
            switch (this.options.MemoryMetric)
            {
                case MemoryMetric.PeakWorkingSet:
                    return result.PeakWorkingSetBytes;
                case MemoryMetric.Max:
                    return Math.Max(result.PeakCommitBytes, result.PeakWorkingSetBytes);
                default:
                    return result.PeakCommitBytes;
            }
        }
    }
}
