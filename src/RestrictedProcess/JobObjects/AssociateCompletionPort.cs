// <copyright file="AssociateCompletionPort.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.JobObjects
{
    using System;
    using System.Runtime.InteropServices;

    /// <summary>
    /// JOBOBJECT_ASSOCIATE_COMPLETION_PORT.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct AssociateCompletionPort
    {
        public IntPtr CompletionKey;
        public IntPtr CompletionPort;
    }
}
