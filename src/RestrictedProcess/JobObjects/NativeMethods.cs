// <copyright file="NativeMethods.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.JobObjects
{
    using System;
    using System.Runtime.InteropServices;

    internal static class NativeMethods
    {
        public const uint Infinite = 0xFFFFFFFF;

        public static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeJobObjectHandle CreateJobObject(IntPtr jobAttributes, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetInformationJobObject(
            SafeJobObjectHandle job,
            InfoClass infoType,
            IntPtr jobObjectInfo,
            uint jobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool QueryInformationJobObject(
            SafeJobObjectHandle job,
            InfoClass jobObjectInformationClass,
            IntPtr jobObjectInfo,
            uint jobObjectInfoLength,
            IntPtr returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AssignProcessToJobObject(SafeJobObjectHandle job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool TerminateJobObject(SafeJobObjectHandle job, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern SafeIoCompletionPortHandle CreateIoCompletionPort(
            IntPtr fileHandle,
            IntPtr existingCompletionPort,
            IntPtr completionKey,
            uint numberOfConcurrentThreads);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetQueuedCompletionStatus(
            SafeIoCompletionPortHandle completionPort,
            out uint numberOfBytes,
            out IntPtr completionKey,
            out IntPtr overlapped,
            uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PostQueuedCompletionStatus(
            SafeIoCompletionPortHandle completionPort,
            uint numberOfBytes,
            IntPtr completionKey,
            IntPtr overlapped);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr obj);
    }
}
