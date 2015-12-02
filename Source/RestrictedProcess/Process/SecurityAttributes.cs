// <copyright file="SecurityAttributes.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.Process
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Runtime.InteropServices;

    [SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1401:FieldsMustBePrivate", Justification = "Reviewed. Suppression is OK here.")]
    [StructLayout(LayoutKind.Sequential)]
    public class SecurityAttributes
    {
        public int Length = 12;

        public SafeLocalMemHandle SecurityDescriptor = new SafeLocalMemHandle(IntPtr.Zero, false);

        public bool InheritHandle = false;
    }
}
