// <copyright file="ProcessMitigations2.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess
{
    using System;

    /// <summary>
    /// The second 64-bit word of process creation mitigation policies
    /// (PROCESS_CREATION_MITIGATION_POLICY2_*), introduced in Windows 10 1703. Setting any of these makes
    /// the sandbox pass a 16-byte mitigation attribute instead of an 8-byte one; on an older build the
    /// attribute is silently narrowed back to the first word.
    /// <para>
    /// Each field in the native policy is two bits wide, so the values below are the ALWAYS_ON patterns
    /// rather than single bits. All of them are off by default: several carry a measurable performance
    /// cost, which matters when the sandbox is being used to time a program.
    /// </para>
    /// </summary>
    [Flags]
    public enum ProcessMitigations2 : ulong
    {
        /// <summary>
        /// No second-word mitigation policies.
        /// </summary>
        None = 0,

        /// <summary>
        /// Requires loaded images to satisfy loader integrity continuity checks.
        /// </summary>
        LoaderIntegrityContinuity = 0x1UL << 4,

        /// <summary>
        /// Enables strict Control Flow Guard: images without CFG metadata fail to load.
        /// </summary>
        StrictControlFlowGuard = 0x1UL << 8,

        /// <summary>
        /// Remaps a clean copy of the main image when import table tampering is detected.
        /// </summary>
        ModuleTamperingProtection = 0x1UL << 12,

        /// <summary>
        /// Stops hyperthreads from influencing this process's indirect branch predictions (Spectre v2).
        /// Costs performance.
        /// </summary>
        RestrictIndirectBranchPrediction = 0x1UL << 16,

        /// <summary>
        /// Disables speculative store bypass (Spectre v4). Costs performance.
        /// </summary>
        SpeculativeStoreBypassDisable = 0x1UL << 24,

        /// <summary>
        /// Enables CET user-mode shadow stacks (hardware-enforced stack protection).
        /// </summary>
        CetUserShadowStacks = 0x1UL << 28,

        /// <summary>
        /// Enables CET user-mode shadow stacks in strict mode, where images without CET metadata are
        /// refused. Be careful applying this to anything that loads third-party code.
        /// </summary>
        CetUserShadowStacksStrictMode = 0x3UL << 28,

        /// <summary>
        /// Validates the instruction pointer passed to SetThreadContext against the shadow stack.
        /// </summary>
        UserCetSetContextIpValidation = 0x1UL << 32,

        /// <summary>
        /// Blocks the load of binaries that are not CET compatible.
        /// </summary>
        BlockNonCetBinaries = 0x1UL << 36,

        /// <summary>
        /// Enables extended Control Flow Guard (XFG).
        /// </summary>
        ExtendedControlFlowGuard = 0x1UL << 40,

        /// <summary>
        /// Keeps threads of this process from sharing a physical core with threads outside its security
        /// domain. Windows 11 24H2 and later; reduces cross-process side channels at a scheduling cost.
        /// </summary>
        RestrictCoreSharing = 0x1UL << 52,

        /// <summary>
        /// Blocks FSCTL_* control codes sent to NtFsControlFile, apart from the documented named pipe
        /// exceptions. A large and rarely needed kernel attack surface for a console program, which makes
        /// this the second-word mitigation most worth turning on for untrusted code.
        /// </summary>
        FsctlSystemCallDisable = 0x1UL << 56,
    }
}
