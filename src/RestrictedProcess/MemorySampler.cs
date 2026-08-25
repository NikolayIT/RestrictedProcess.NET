// <copyright file="MemorySampler.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess
{
    using System;
    using System.ComponentModel;
    using System.Threading;

    using DiagnosticsProcess = System.Diagnostics.Process;

    /// <summary>
    /// Watches how much memory an ordinary process is using, for
    /// <see cref="StandardProcessExecutor"/>.
    /// <para>
    /// Sampling is necessary here and only here. Once a process has exited,
    /// <see cref="DiagnosticsProcess.PeakWorkingSet64"/> throws rather than returning the figure it
    /// reached, so reading the counters after the wait - which is what this executor used to do - always
    /// produced zero. The sandboxed executor has no such problem: its job object keeps the peak after every
    /// process in it is gone, which is exactly why it needs no sampling thread.
    /// </para>
    /// </summary>
    internal sealed class MemorySampler : IDisposable
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(50);

        private readonly DiagnosticsProcess process;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly Thread thread;

        private long peakWorkingSetBytes;
        private long peakCommitBytes;
        private int stopped;

        public MemorySampler(DiagnosticsProcess process)
        {
            this.process = process;
            this.thread = new Thread(this.Sample, 128 * 1024)
            {
                IsBackground = true,
                Name = "RestrictedProcess memory sampler",
            };
            this.thread.Start();
        }

        public long PeakWorkingSetBytes => Interlocked.Read(ref this.peakWorkingSetBytes);

        /// <summary>
        /// Gets the largest private (committed) size observed. This is the running maximum of a current
        /// value rather than a peak the OS maintains, so a spike between two samples can be missed.
        /// </summary>
        public long PeakCommitBytes => Interlocked.Read(ref this.peakCommitBytes);

        /// <summary>
        /// Takes a final reading and stops sampling. Call this as soon as the process has been waited for.
        /// </summary>
        public void Stop()
        {
            if (Interlocked.Exchange(ref this.stopped, 1) != 0)
            {
                return;
            }

            this.cancellation.Cancel();
            this.thread.Join(TimeSpan.FromSeconds(2));
        }

        public void Dispose()
        {
            this.Stop();
            this.cancellation.Dispose();
        }

        private static void Max(ref long target, long candidate)
        {
            long current;
            while ((current = Interlocked.Read(ref target)) < candidate)
            {
                if (Interlocked.CompareExchange(ref target, candidate, current) == current)
                {
                    return;
                }
            }
        }

        private void Sample()
        {
            while (true)
            {
                try
                {
                    this.process.Refresh();
                    Max(ref this.peakWorkingSetBytes, this.process.PeakWorkingSet64);
                    Max(ref this.peakCommitBytes, this.process.PrivateMemorySize64);
                }
                catch (InvalidOperationException)
                {
                    // The process has exited or was never started; whatever was seen so far is the answer.
                    return;
                }
                catch (Win32Exception)
                {
                    return;
                }

                if (this.cancellation.Token.WaitHandle.WaitOne(Interval))
                {
                    return;
                }
            }
        }
    }
}
