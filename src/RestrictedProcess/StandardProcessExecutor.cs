// <copyright file="StandardProcessExecutor.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Abstractions;

    using DiagnosticsProcess = System.Diagnostics.Process;

    /// <summary>
    /// Runs a program with a plain <see cref="System.Diagnostics.Process"/>, with no sandbox at all.
    /// <para>
    /// This is a convenience for trusted programs and for comparing against the sandboxed path. It applies
    /// the time limit and captures output, but it enforces <em>no</em> memory limit, no privilege
    /// reduction and no isolation: never point it at code you do not trust.
    /// </para>
    /// </summary>
    public sealed class StandardProcessExecutor : IExecutor
    {
        private readonly ILogger logger;
        private readonly RestrictedProcessOptions options;

        /// <summary>
        /// Initializes a new instance of the <see cref="StandardProcessExecutor"/> class.
        /// </summary>
        /// <param name="logger">An optional logger for diagnostics.</param>
        public StandardProcessExecutor(ILogger? logger = null)
            : this(new RestrictedProcessOptions(), logger)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="StandardProcessExecutor"/> class.
        /// </summary>
        /// <param name="options">Options; only the output caps, encoding, working directory and time
        /// multiplier are honoured here.</param>
        /// <param name="logger">An optional logger for diagnostics.</param>
        public StandardProcessExecutor(RestrictedProcessOptions options, ILogger? logger = null)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.logger = logger ?? NullLogger.Instance;
        }

        /// <inheritdoc/>
        public ProcessExecutionResult Execute(ExecutionRequest request)
        {
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
            var startInfo = new ProcessStartInfo(request.FileName)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = request.WorkingDirectory
                                   ?? this.options.WorkingDirectory
                                   ?? Path.GetDirectoryName(Path.GetFullPath(request.FileName))
                                   ?? string.Empty,
            };

            if (request.Arguments != null)
            {
                foreach (var argument in request.Arguments)
                {
                    startInfo.ArgumentList.Add(argument);
                }
            }

            if (this.options.Encoding != null)
            {
                startInfo.StandardOutputEncoding = this.options.Encoding;
                startInfo.StandardErrorEncoding = this.options.Encoding;
            }

            var deadlineReached = false;
            var cancelled = false;

            using (var process = new DiagnosticsProcess { StartInfo = startInfo })
            {
                if (!process.Start())
                {
                    throw new SandboxException("Could not start " + request.FileName + ".");
                }

                var outputTask = ReadBoundedAsync(process.StandardOutput, this.options.MaxOutputSize);
                var errorTask = ReadBoundedAsync(process.StandardError, this.options.MaxErrorSize);
                var inputTask = WriteInputAsync(process, request.Input);

                var wallClockLimit = this.ResolveWallClockLimit(request);

                try
                {
                    if (wallClockLimit.HasValue)
                    {
                        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                        {
                            deadline.CancelAfter(wallClockLimit.Value);
                            try
                            {
                                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                            {
                                deadlineReached = !cancellationToken.IsCancellationRequested;
                                cancelled = cancellationToken.IsCancellationRequested;
                                Kill(process);
                            }
                        }
                    }
                    else
                    {
                        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                    Kill(process);
                }

                // WaitForExitAsync returns once the process object signals; give the redirected streams the
                // chance to reach end of file before reading what they captured.
                var readers = Task.WhenAll(outputTask, errorTask, inputTask);
                if (await Task.WhenAny(readers, Task.Delay(this.options.OutputDrainTimeout)).ConfigureAwait(false) != readers)
                {
                    this.logger.LogWarning("Standard IO did not reach end of file within {Timeout}.", this.options.OutputDrainTimeout);
                }

                var output = outputTask.Status == TaskStatus.RanToCompletion ? outputTask.Result : (string.Empty, false);
                var error = errorTask.Status == TaskStatus.RanToCompletion ? errorTask.Result : (string.Empty, false);

                result.ReceivedOutput = output.Item1;
                result.OutputTruncated = output.Item2;
                result.ErrorOutput = error.Item1;
                result.ErrorTruncated = error.Item2;

                process.Refresh();
                result.PeakWorkingSetBytes = SafeRead(() => process.PeakWorkingSet64);
                result.PeakCommitBytes = SafeRead(() => process.PeakPagedMemorySize64);
                result.ExitCode = process.HasExited ? process.ExitCode : -1;
                result.TimeWorked = process.HasExited ? process.ExitTime - process.StartTime : TimeSpan.Zero;
                result.UserProcessorTime = SafeRead(() => process.UserProcessorTime);
                result.PrivilegedProcessorTime = SafeRead(() => process.PrivilegedProcessorTime);
            }

            result.MemoryUsed = this.options.MemoryMetric == MemoryMetric.PeakWorkingSet
                ? result.PeakWorkingSetBytes
                : Math.Max(result.PeakCommitBytes, result.PeakWorkingSetBytes);

            this.Classify(request, result, deadlineReached, cancelled);
            return result;
        }

        private static void Kill(DiagnosticsProcess process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
        }

        private static T SafeRead<T>(Func<T> read)
        {
            try
            {
                return read();
            }
            catch (InvalidOperationException)
            {
                return default!;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return default!;
            }
        }

        private static async Task<(string Text, bool Truncated)> ReadBoundedAsync(StreamReader reader, long maxCharacters)
        {
            var builder = new StringBuilder();
            var buffer = new char[8192];
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
                        continue;
                    }

                    builder.Append(buffer, 0, read);
                }
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }

            return (builder.ToString(), truncated);
        }

        private static async Task WriteInputAsync(DiagnosticsProcess process, string input)
        {
            try
            {
                await process.StandardInput.WriteLineAsync(input ?? string.Empty).ConfigureAwait(false);
                await process.StandardInput.FlushAsync().ConfigureAwait(false);
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                try
                {
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

            return TimeSpan.FromMilliseconds(
                request.CpuTimeLimit.Value.TotalMilliseconds * Math.Max(1.0, this.options.WallClockWaitMultiplier));
        }

        private void Classify(ExecutionRequest request, ProcessExecutionResult result, bool deadlineReached, bool cancelled)
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

            if (!string.IsNullOrEmpty(result.ErrorOutput))
            {
                result.Type = ProcessExecutionResultType.RunTimeError;
            }

            if (request.MemoryLimitBytes.HasValue && result.MemoryUsed > request.MemoryLimitBytes.Value)
            {
                result.Type = ProcessExecutionResultType.MemoryLimit;
            }

            if (this.options.TreatNonZeroExitCodeAsRunTimeError
                && result.ExitCode != 0
                && result.Type == ProcessExecutionResultType.Success)
            {
                result.Type = ProcessExecutionResultType.RunTimeError;
            }

            if ((result.OutputTruncated || result.ErrorTruncated)
                && (result.Type == ProcessExecutionResultType.Success || result.Type == ProcessExecutionResultType.RunTimeError))
            {
                result.Type = ProcessExecutionResultType.OutputLimit;
            }
        }
    }
}
