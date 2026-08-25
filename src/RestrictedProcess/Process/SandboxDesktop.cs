// <copyright file="SandboxDesktop.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.Process
{
    using System;
    using System.ComponentModel;
    using System.Runtime.InteropServices;

    /// <summary>
    /// A throwaway desktop the sandboxed process runs on, so it cannot enumerate,
    /// read or send window messages to windows on the interactive desktop.
    /// The desktop's security descriptor grants access to the restricting SIDs of the
    /// sandbox token (Everyone and RESTRICTED) and carries a Low mandatory label so a
    /// Low integrity process may use it.
    /// </summary>
    internal sealed class SandboxDesktop : IDisposable
    {
        // DesktopCreateWindow | DesktopEnumerate | DesktopWriteObjects | DesktopReadObjects | ... = DESKTOP full access.
        private const uint GenericAll = 0x10000000;

        // Grant GENERIC_ALL to Everyone (WD), RESTRICTED (RC) and ALL APPLICATION PACKAGES (AC, so a
        // network-blocked AppContainer process can use the desktop); label it Low integrity (ML;;NW;;;LW).
        private const string DesktopSecurityDescriptor = "D:(A;;GA;;;WD)(A;;GA;;;RC)(A;;GA;;;AC)S:(ML;;NW;;;LW)";

        private IntPtr handle;

        public SandboxDesktop()
        {
            this.Name = "rp_" + Guid.NewGuid().ToString("N");

            SafeLocalMemHandle? securityDescriptor = null;
            var securityAttributes = IntPtr.Zero;
            try
            {
                if (!NativeMethods.ConvertStringSecurityDescriptorToSecurityDescriptor(
                        DesktopSecurityDescriptor,
                        1, // SDDL_REVISION_1
                        out securityDescriptor,
                        IntPtr.Zero))
                {
                    throw new Win32Exception();
                }

                var nativeAttributes = new SecurityAttributesNative
                {
                    Length = Marshal.SizeOf<SecurityAttributesNative>(),
                    SecurityDescriptor = securityDescriptor.DangerousGetHandle(),
                    InheritHandle = false,
                };
                securityAttributes = Marshal.AllocHGlobal(nativeAttributes.Length);
                Marshal.StructureToPtr(nativeAttributes, securityAttributes, false);

                this.handle = NativeMethods.CreateDesktop(this.Name, IntPtr.Zero, IntPtr.Zero, 0, GenericAll, securityAttributes);
                if (this.handle == IntPtr.Zero)
                {
                    throw new Win32Exception();
                }
            }
            finally
            {
                if (securityAttributes != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(securityAttributes);
                }

                securityDescriptor?.Dispose();
            }
        }

        ~SandboxDesktop()
        {
            this.Dispose();
        }

        public string Name { get; }

        public void Dispose()
        {
            if (this.handle != IntPtr.Zero)
            {
                NativeMethods.CloseDesktop(this.handle);
                this.handle = IntPtr.Zero;
            }

            GC.SuppressFinalize(this);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SecurityAttributesNative
        {
            public int Length;

            public IntPtr SecurityDescriptor;

            [MarshalAs(UnmanagedType.Bool)]
            public bool InheritHandle;
        }
    }
}
