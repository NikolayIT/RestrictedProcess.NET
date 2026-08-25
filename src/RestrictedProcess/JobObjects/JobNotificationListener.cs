// <copyright file="JobNotificationListener.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.JobObjects
{
    using System;
    using System.Threading;

    /// <summary>
    /// Pumps the I/O completion port associated with a job object so that a limit breach, and the exit of
    /// the last process in the tree, are observed the moment they happen.
    /// <para>
    /// This replaces the old 45 ms sampling loop. It costs one background thread per execution, but that
    /// thread blocks in the kernel instead of waking twenty times a second, and the job tells us which
    /// limit was exceeded rather than leaving us to infer it afterwards.
    /// </para>
    /// </summary>
    internal sealed class JobNotificationListener : IDisposable
    {
        private static readonly IntPtr JobCompletionKey = new IntPtr(1);
        private static readonly IntPtr StopCompletionKey = new IntPtr(2);

        private readonly SafeIoCompletionPortHandle completionPort;
        private readonly Thread pumpThread;
        private readonly ManualResetEventSlim allProcessesExited = new ManualResetEventSlim(false);
        private readonly ManualResetEventSlim notificationLimitReached = new ManualResetEventSlim(false);

        private int notificationLimitHit;
        private int disposed;

        public JobNotificationListener(JobObject job)
        {
            this.completionPort = NativeMethods.CreateIoCompletionPort(
                NativeMethods.InvalidHandleValue, IntPtr.Zero, IntPtr.Zero, 1);
            if (this.completionPort.IsInvalid)
            {
                throw SandboxException.FromLastWin32Error(
                    SandboxStep.AssociateJobCompletionPort, "CreateIoCompletionPort");
            }

            job.AssociateCompletionPort(this.completionPort, JobCompletionKey);

            this.pumpThread = new Thread(this.Pump, 256 * 1024)
            {
                IsBackground = true,
                Name = "RestrictedProcess job notifications",
            };
            this.pumpThread.Start();
        }

        /// <summary>
        /// Gets a value indicating whether a notification limit (committed memory or processor time) was
        /// reported as exceeded by the job object.
        /// </summary>
        public bool NotificationLimitExceeded => Volatile.Read(ref this.notificationLimitHit) != 0;

        /// <summary>
        /// Gets a handle that is set once every process in the job has exited. Unlike waiting on the root
        /// process handle this also covers descendants that outlive their parent.
        /// </summary>
        public WaitHandle AllProcessesExited => this.allProcessesExited.WaitHandle;

        /// <summary>
        /// Gets a handle that is set as soon as the job reports a notification limit breach, so the
        /// executor can stop a program the moment it goes over instead of letting it run out the clock.
        /// </summary>
        public WaitHandle NotificationLimitReached => this.notificationLimitReached.WaitHandle;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref this.disposed, 1) != 0)
            {
                return;
            }

            if (!this.completionPort.IsInvalid && !this.completionPort.IsClosed)
            {
                NativeMethods.PostQueuedCompletionStatus(this.completionPort, 0, StopCompletionKey, IntPtr.Zero);
            }

            // The pump only ever blocks inside GetQueuedCompletionStatus, which the post above releases.
            this.pumpThread.Join(TimeSpan.FromSeconds(5));
            this.completionPort.Dispose();
            this.allProcessesExited.Dispose();
            this.notificationLimitReached.Dispose();
        }

        private void Pump()
        {
            while (true)
            {
                if (!NativeMethods.GetQueuedCompletionStatus(
                        this.completionPort,
                        out var messageId,
                        out var completionKey,
                        out _,
                        NativeMethods.Infinite))
                {
                    // The port was closed underneath us or the wait failed; either way there is nothing
                    // left to pump.
                    return;
                }

                if (completionKey == StopCompletionKey)
                {
                    return;
                }

                switch ((JobMessage)messageId)
                {
                    case JobMessage.NotificationLimit:
                    case JobMessage.JobMemoryLimit:
                    case JobMessage.ProcessMemoryLimit:
                    case JobMessage.EndOfJobTime:
                    case JobMessage.EndOfProcessTime:
                        Volatile.Write(ref this.notificationLimitHit, 1);
                        this.notificationLimitReached.Set();
                        break;

                    case JobMessage.ActiveProcessZero:
                        this.allProcessesExited.Set();
                        break;
                }
            }
        }
    }
}
