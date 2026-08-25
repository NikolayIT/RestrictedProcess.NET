// <copyright file="JobObject.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.JobObjects
{
    using System;
    using System.Runtime.InteropServices;

    /// <summary>
    /// A Win32 job object holding the sandboxed process tree. The handle is created before the process
    /// so it can be attached at creation time through PROC_THREAD_ATTRIBUTE_JOB_LIST, which removes the
    /// window between CreateProcess and AssignProcessToJobObject entirely.
    /// </summary>
    internal sealed class JobObject : IDisposable
    {
        private readonly SafeJobObjectHandle handle;

        public JobObject()
        {
            this.handle = NativeMethods.CreateJobObject(IntPtr.Zero, null);
            if (this.handle.IsInvalid)
            {
                throw SandboxException.FromLastWin32Error(SandboxStep.CreateJobObject);
            }
        }

        public SafeJobObjectHandle Handle => this.handle;

        public void SetExtendedLimitInformation(ExtendedLimitInformation extendedInfo)
        {
            this.SetInformation(InfoClass.ExtendedLimitInformation, extendedInfo, SandboxStep.SetJobLimits);
        }

        public void SetBasicUiRestrictions(BasicUiRestrictions uiRestrictions)
        {
            this.SetInformation(InfoClass.BasicUiRestrictions, uiRestrictions, SandboxStep.SetJobUiRestrictions);
        }

        public void SetCpuRateControlInformation(CpuRateControlInformation cpuRateControlInformation)
        {
            this.SetInformation(InfoClass.CpuRateControlInformation, cpuRateControlInformation, SandboxStep.SetJobCpuRate);
        }

        /// <summary>
        /// Applies the soft notification limits. Returns false when the OS rejects them, so the caller can
        /// fall back to comparing the accounting totals after the run instead of failing the execution.
        /// </summary>
        public bool TrySetNotificationLimits(NotificationLimitInformation notificationLimits)
        {
            return this.TrySetInformation(InfoClass.NotificationLimitInformation, notificationLimits);
        }

        public void AssociateCompletionPort(SafeIoCompletionPortHandle completionPort, IntPtr completionKey)
        {
            var info = new AssociateCompletionPort
            {
                CompletionKey = completionKey,
                CompletionPort = completionPort.DangerousGetHandle(),
            };

            this.SetInformation(InfoClass.AssociateCompletionPortInformation, info, SandboxStep.AssociateJobCompletionPort);
        }

        public ExtendedLimitInformation GetExtendedLimitInformation()
        {
            return this.QueryInformation<ExtendedLimitInformation>(InfoClass.ExtendedLimitInformation);
        }

        public BasicAndIoAccountingInformation GetAccountingInformation()
        {
            return this.QueryInformation<BasicAndIoAccountingInformation>(InfoClass.BasicAndIoAccountingInformation);
        }

        /// <summary>
        /// Reads the notification limits back. Mostly useful as a check that the structure marshals the way
        /// the kernel expects: if the layout were wrong, what comes back would not be what went in.
        /// </summary>
        public NotificationLimitInformation GetNotificationLimits()
        {
            return this.QueryInformation<NotificationLimitInformation>(InfoClass.NotificationLimitInformation);
        }

        public LimitViolationInformation GetLimitViolationInformation()
        {
            return this.QueryInformation<LimitViolationInformation>(InfoClass.LimitViolationInformation);
        }

        public bool AddProcess(IntPtr processHandle)
        {
            return NativeMethods.AssignProcessToJobObject(this.handle, processHandle);
        }

        public bool Terminate(uint exitCode)
        {
            return !this.handle.IsInvalid
                   && !this.handle.IsClosed
                   && NativeMethods.TerminateJobObject(this.handle, exitCode);
        }

        public void Dispose()
        {
            // The job carries JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE, so releasing the last handle also kills
            // anything still running inside it.
            this.handle.Dispose();
        }

        private void SetInformation<T>(InfoClass infoClass, T value, SandboxStep step)
            where T : struct
        {
            if (!this.TrySetInformation(infoClass, value))
            {
                throw SandboxException.FromLastWin32Error(step, infoClass.ToString());
            }
        }

        private bool TrySetInformation<T>(InfoClass infoClass, T value)
            where T : struct
        {
            var length = Marshal.SizeOf<T>();
            var pointer = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(value, pointer, false);
                return NativeMethods.SetInformationJobObject(this.handle, infoClass, pointer, (uint)length);
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
        }

        private T QueryInformation<T>(InfoClass infoClass)
            where T : struct
        {
            var length = Marshal.SizeOf<T>();
            var pointer = Marshal.AllocHGlobal(length);
            try
            {
                if (!NativeMethods.QueryInformationJobObject(this.handle, infoClass, pointer, (uint)length, IntPtr.Zero))
                {
                    throw SandboxException.FromLastWin32Error(SandboxStep.QueryJobInformation, infoClass.ToString());
                }

                return Marshal.PtrToStructure<T>(pointer);
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
        }
    }
}
