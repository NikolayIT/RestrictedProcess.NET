// <copyright file="SafeIoCompletionPortHandle.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.JobObjects
{
    using Microsoft.Win32.SafeHandles;

    internal sealed class SafeIoCompletionPortHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal SafeIoCompletionPortHandle()
            : base(true)
        {
        }

        protected override bool ReleaseHandle()
        {
            return NativeMethods.CloseHandle(this.handle);
        }
    }
}
