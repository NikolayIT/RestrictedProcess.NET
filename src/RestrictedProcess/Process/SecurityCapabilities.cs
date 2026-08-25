// <copyright file="SecurityCapabilities.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.Process
{
    using System;
    using System.Runtime.InteropServices;

    /// <summary>
    /// The SECURITY_CAPABILITIES structure that turns a created process into an AppContainer.
    /// Passed through the PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES attribute. Leaving
    /// <see cref="Capabilities"/> empty grants the AppContainer no capabilities, so the Windows
    /// Firewall denies it network access.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SecurityCapabilities
    {
        public IntPtr AppContainerSid;

        public IntPtr Capabilities;

        public uint CapabilityCount;

        public uint Reserved;
    }
}
