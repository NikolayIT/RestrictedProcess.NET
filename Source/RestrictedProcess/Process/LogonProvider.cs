// <copyright file="LogonProvider.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.Process
{
    internal enum LogonProvider
    {
        LOGON32_PROVIDER_DEFAULT,
        LOGON32_PROVIDER_WINNT35,
        LOGON32_PROVIDER_WINNT40,
        LOGON32_PROVIDER_WINNT50
    }
}
