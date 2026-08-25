// <copyright file="SandboxException.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess
{
    using System;
    using System.ComponentModel;
    using System.Globalization;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Thrown when a step of the sandbox setup fails. Unlike a bare <see cref="Win32Exception"/> it
    /// records which step failed, which is what makes an "Access is denied" raised from deep inside
    /// process creation diagnosable.
    /// </summary>
    public class SandboxException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SandboxException"/> class.
        /// </summary>
        public SandboxException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SandboxException"/> class.
        /// </summary>
        /// <param name="message">The message describing the failure.</param>
        public SandboxException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SandboxException"/> class.
        /// </summary>
        /// <param name="message">The message describing the failure.</param>
        /// <param name="innerException">The exception that caused this one.</param>
        public SandboxException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        private SandboxException(SandboxStep step, int nativeErrorCode, string message, Exception? innerException)
            : base(message, innerException)
        {
            this.Step = step;
            this.NativeErrorCode = nativeErrorCode;
        }

        /// <summary>
        /// Gets the sandbox setup step that failed.
        /// </summary>
        public SandboxStep Step { get; } = SandboxStep.Unknown;

        /// <summary>
        /// Gets the Win32 error code returned by the failing call, or zero when the failure was not a Win32 error.
        /// </summary>
        public int NativeErrorCode { get; }

        /// <summary>
        /// Creates an exception for a Win32 call that failed, reading the error from
        /// <see cref="Marshal.GetLastWin32Error"/>. Call this immediately after the failing P/Invoke.
        /// </summary>
        /// <param name="step">The step that failed.</param>
        /// <param name="detail">Extra context, for example the name of the object being created.</param>
        /// <returns>The exception to throw.</returns>
        public static SandboxException FromLastWin32Error(SandboxStep step, string? detail = null)
        {
            return FromWin32Error(step, Marshal.GetLastWin32Error(), detail);
        }

        /// <summary>
        /// Creates an exception for a Win32 call that failed with a known error code.
        /// </summary>
        /// <param name="step">The step that failed.</param>
        /// <param name="nativeErrorCode">The Win32 error code or HRESULT returned by the call.</param>
        /// <param name="detail">Extra context, for example the name of the object being created.</param>
        /// <returns>The exception to throw.</returns>
        public static SandboxException FromWin32Error(SandboxStep step, int nativeErrorCode, string? detail = null)
        {
            var inner = new Win32Exception(nativeErrorCode);
            var message = string.Format(
                CultureInfo.InvariantCulture,
                "Sandbox step {0} failed with 0x{1:X8}: {2}{3}",
                step,
                nativeErrorCode,
                inner.Message,
                string.IsNullOrEmpty(detail) ? string.Empty : " (" + detail + ")");

            return new SandboxException(step, nativeErrorCode, message, inner);
        }

        /// <summary>
        /// Creates an exception for a step that failed without a Win32 error code.
        /// </summary>
        /// <param name="step">The step that failed.</param>
        /// <param name="detail">A description of the failure.</param>
        /// <returns>The exception to throw.</returns>
        public static SandboxException For(SandboxStep step, string detail)
        {
            var message = string.Format(CultureInfo.InvariantCulture, "Sandbox step {0} failed: {1}", step, detail);
            return new SandboxException(step, 0, message, null);
        }
    }
}
