// <copyright file="ProcessMitigations.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess
{
    using System;

    /// <summary>
    /// Process creation mitigation policies applied to the sandboxed process through
    /// the PROC_THREAD_ATTRIBUTE_MITIGATION_POLICY attribute. The values match the
    /// PROCESS_CREATION_MITIGATION_POLICY_* constants from the Windows SDK.
    /// </summary>
    [Flags]
    public enum ProcessMitigations : ulong
    {
        /// <summary>
        /// No mitigation policies.
        /// </summary>
        None = 0,

        /// <summary>
        /// Enables data execution prevention (DEP) for the child process.
        /// </summary>
        DepEnable = 0x01,

        /// <summary>
        /// Enables structured exception handler overwrite protection (SEHOP) for the child process.
        /// </summary>
        SehOp = 0x04,

        /// <summary>
        /// Forces relocation of images not built with /DYNAMICBASE; images without a relocation section fail to load.
        /// </summary>
        ForceRelocateImages = 0x1UL << 8,

        /// <summary>
        /// Terminates the process immediately on heap corruption.
        /// </summary>
        HeapTerminate = 0x1UL << 12,

        /// <summary>
        /// Enables bottom-up randomization of virtual memory allocations.
        /// </summary>
        BottomUpAslr = 0x1UL << 16,

        /// <summary>
        /// Increases the entropy used by bottom-up randomization (64-bit processes).
        /// </summary>
        HighEntropyAslr = 0x1UL << 20,

        /// <summary>
        /// Raises an exception immediately on any invalid handle reference instead of failing the call.
        /// </summary>
        StrictHandleChecks = 0x1UL << 24,

        /// <summary>
        /// Prevents the process from making win32k.sys system calls.
        /// WARNING: user32.dll cannot initialize under this policy, so .NET Framework
        /// executables (and most other programs) fail to start with it enabled.
        /// </summary>
        Win32kSystemCallDisable = 0x1UL << 28,

        /// <summary>
        /// Disables legacy extension points (AppInit DLLs, window hooks, winsock LSPs, IME) inside the process.
        /// </summary>
        ExtensionPointDisable = 0x1UL << 32,

        /// <summary>
        /// Prevents the process from generating or modifying executable code.
        /// WARNING: this blocks the JIT, so .NET executables fail under this policy.
        /// </summary>
        ProhibitDynamicCode = 0x1UL << 36,

        /// <summary>
        /// Prevents the process from loading DLLs that are not signed by Microsoft.
        /// </summary>
        BlockNonMicrosoftBinaries = 0x1UL << 44,

        /// <summary>
        /// Prevents the process from loading non-system fonts.
        /// </summary>
        FontDisable = 0x1UL << 48,

        /// <summary>
        /// Prevents the process from loading images from remote devices (UNC paths, WebDAV).
        /// </summary>
        ImageLoadNoRemote = 0x1UL << 52,

        /// <summary>
        /// Prevents the process from loading images with a Low mandatory label (written by low-integrity processes).
        /// </summary>
        ImageLoadNoLowLabel = 0x1UL << 56,

        /// <summary>
        /// Searches %SystemRoot%\system32 first when loading images.
        /// </summary>
        PreferSystem32 = 0x1UL << 60,

        /// <summary>
        /// The default set: every mitigation a plain console executable (including .NET Framework ones) tolerates.
        /// </summary>
        Default = DepEnable
                  | SehOp
                  | HeapTerminate
                  | BottomUpAslr
                  | HighEntropyAslr
                  | StrictHandleChecks
                  | ExtensionPointDisable
                  | ImageLoadNoRemote
                  | ImageLoadNoLowLabel
                  | PreferSystem32,
    }
}
