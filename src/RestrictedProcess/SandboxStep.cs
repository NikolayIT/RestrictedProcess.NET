// <copyright file="SandboxStep.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess
{
    /// <summary>
    /// Identifies the step of the sandbox setup that failed, so a <see cref="SandboxException"/>
    /// says which Win32 operation returned the error instead of only reporting its message.
    /// </summary>
    public enum SandboxStep
    {
        /// <summary>The step is not known.</summary>
        Unknown = 0,

        /// <summary>Opening the primary token of the current process.</summary>
        OpenProcessToken,

        /// <summary>Querying information from an access token.</summary>
        QueryTokenInformation,

        /// <summary>Building the restricted token with CreateRestrictedToken.</summary>
        CreateRestrictedToken,

        /// <summary>Applying the mandatory integrity label to the token.</summary>
        SetTokenIntegrityLevel,

        /// <summary>Replacing the default DACL of the token.</summary>
        SetTokenDefaultDacl,

        /// <summary>Adding no-read-up and no-execute-up to the token's own mandatory label.</summary>
        HardenTokenIntegrityPolicy,

        /// <summary>Re-enabling a privilege on the restricted token.</summary>
        AdjustTokenPrivileges,

        /// <summary>Creating or converting a security identifier.</summary>
        CreateSid,

        /// <summary>Building an access control list or security descriptor.</summary>
        BuildSecurityDescriptor,

        /// <summary>Creating the throwaway desktop.</summary>
        CreateDesktop,

        /// <summary>Creating the alternate window station.</summary>
        CreateWindowStation,

        /// <summary>Applying the mandatory label to the desktop or window station.</summary>
        SetWindowObjectIntegrityLevel,

        /// <summary>Creating the job object.</summary>
        CreateJobObject,

        /// <summary>Applying the extended limit information to the job object.</summary>
        SetJobLimits,

        /// <summary>Applying the user interface restrictions to the job object.</summary>
        SetJobUiRestrictions,

        /// <summary>Applying the CPU rate control information to the job object.</summary>
        SetJobCpuRate,

        /// <summary>Applying the notification limits used for event-driven limit detection.</summary>
        SetJobNotificationLimits,

        /// <summary>Associating the job object with an I/O completion port.</summary>
        AssociateJobCompletionPort,

        /// <summary>Reading accounting or limit information back from the job object.</summary>
        QueryJobInformation,

        /// <summary>Assigning the created process to the job object.</summary>
        AssignProcessToJob,

        /// <summary>Creating the AppContainer profile used for network blocking.</summary>
        CreateAppContainerProfile,

        /// <summary>Granting the AppContainer identity access to the executable.</summary>
        GrantAppContainerAccess,

        /// <summary>Allocating the PROC_THREAD_ATTRIBUTE_LIST.</summary>
        InitializeAttributeList,

        /// <summary>Adding an attribute to the PROC_THREAD_ATTRIBUTE_LIST.</summary>
        UpdateAttributeList,

        /// <summary>Creating one of the standard IO pipes.</summary>
        CreatePipe,

        /// <summary>Duplicating a pipe handle so the parent end is not inheritable.</summary>
        DuplicateHandle,

        /// <summary>Creating the sandboxed process with CreateProcessAsUser.</summary>
        CreateProcess,

        /// <summary>Resuming the main thread of the sandboxed process.</summary>
        ResumeThread,

        /// <summary>Reading the process creation, exit and processor times.</summary>
        QueryProcessTimes,

        /// <summary>Reading the memory counters of the process.</summary>
        QueryProcessMemory,
    }
}
