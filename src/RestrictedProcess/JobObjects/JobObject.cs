// <copyright file="JobObject.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.JobObjects
{
    using System;
    using System.ComponentModel;
    using System.Runtime.InteropServices;

    internal class JobObject : IDisposable
    {
        private IntPtr handle;
        private bool disposed;

        public JobObject()
        {
            var attr = default(SecurityAttributes);
            this.handle = NativeMethods.CreateJobObject(ref attr, null);
        }

        ~JobObject()
        {
            this.Dispose(false);
        }

        public void SetExtendedLimitInformation(ExtendedLimitInformation extendedInfo)
        {
            var length = Marshal.SizeOf(typeof(ExtendedLimitInformation));
            var extendedInfoPointer = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(extendedInfo, extendedInfoPointer, false);
                if (!NativeMethods.SetInformationJobObject(this.handle, InfoClass.ExtendedLimitInformation, extendedInfoPointer, (uint)length))
                {
                    throw new Win32Exception();
                }
            }
            finally
            {
                Marshal.FreeHGlobal(extendedInfoPointer);
            }
        }

        public void SetBasicUiRestrictions(BasicUiRestrictions uiRestrictions)
        {
            var length = Marshal.SizeOf(typeof(BasicUiRestrictions));
            var uiRestrictionsInfoPointer = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(uiRestrictions, uiRestrictionsInfoPointer, false);
                if (!NativeMethods.SetInformationJobObject(this.handle, InfoClass.BasicUiRestrictions, uiRestrictionsInfoPointer, (uint)length))
                {
                    throw new Win32Exception();
                }
            }
            finally
            {
                Marshal.FreeHGlobal(uiRestrictionsInfoPointer);
            }
        }

        public ExtendedLimitInformation GetExtendedLimitInformation()
        {
            var length = Marshal.SizeOf(typeof(ExtendedLimitInformation));
            var extendedLimitInformationPointer = Marshal.AllocHGlobal(length);
            try
            {
                if (!NativeMethods.QueryInformationJobObject(this.handle, InfoClass.ExtendedLimitInformation, extendedLimitInformationPointer, (uint)length, IntPtr.Zero))
                {
                    throw new Win32Exception();
                }

                return Marshal.PtrToStructure<ExtendedLimitInformation>(extendedLimitInformationPointer);
            }
            finally
            {
                Marshal.FreeHGlobal(extendedLimitInformationPointer);
            }
        }

        //// // The peak memory used by any process ever associated with the job.
        //// IntPtr PeakProcessMemoryUsed
        //// {
        ////     get
        ////     {
        ////         ExtendedLimitInformation extendedLimitInformation =
        ////             QueryJobInformation<JOBOBJECT_EXTENDED_LIMIT_INFORMATION, JobObjectExtendedLimitInformation>(_hJob);
        ////         return System::IntPtr((void*)extendedLimitInformation.PeakProcessMemoryUsed);
        ////     }
        //// }

        //// // The peak memory usage of all processes currently associated with the job.
        //// System::IntPtr JobObject::PeakJobMemoryUsed::get()
        //// {
        ////     JOBOBJECT_EXTENDED_LIMIT_INFORMATION extendedLimitInformation =
        ////         QueryJobInformation<JOBOBJECT_EXTENDED_LIMIT_INFORMATION, JobObjectExtendedLimitInformation>(_hJob);
        ////     return System::IntPtr((void *)extendedLimitInformation.PeakJobMemoryUsed);
        //// }

        public void Close()
        {
            NativeMethods.CloseHandle(this.handle);
            this.handle = IntPtr.Zero;
        }

        public bool AddProcess(IntPtr processHandle)
        {
            return NativeMethods.AssignProcessToJobObject(this.handle, processHandle);
        }

        public void Dispose()
        {
            this.Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (this.disposed)
                {
                    return;
                }

                this.Close();
                this.disposed = true;
                GC.SuppressFinalize(this);
            }
        }
    }
}
