// <copyright file="SidFactory.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.Process
{
    using System;
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Security.Principal;

    /// <summary>
    /// Helpers for the security identifiers the sandbox needs, including the unique per-run SID.
    /// </summary>
    internal static class SidFactory
    {
        /// <summary>
        /// The RESTRICTED SID (S-1-5-12), the classic marker for "this token is a sandbox".
        /// </summary>
        public static SecurityIdentifier Restricted => new SecurityIdentifier(WellKnownSidType.RestrictedCodeSid, null);

        /// <summary>
        /// The Everyone / World SID (S-1-1-0).
        /// </summary>
        public static SecurityIdentifier Everyone => new SecurityIdentifier(WellKnownSidType.WorldSid, null);

        /// <summary>
        /// The BUILTIN\Users SID (S-1-5-32-545).
        /// </summary>
        public static SecurityIdentifier BuiltinUsers => new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

        /// <summary>
        /// The INTERACTIVE SID (S-1-5-4).
        /// </summary>
        public static SecurityIdentifier Interactive => new SecurityIdentifier(WellKnownSidType.InteractiveSid, null);

        /// <summary>
        /// The NULL SID (S-1-0-0). Used as the only restricting SID of a lockdown token: nothing grants
        /// access to it, so the second access check fails for essentially every securable object.
        /// </summary>
        public static SecurityIdentifier Null => new SecurityIdentifier(WellKnownSidType.NullSid, null);

        /// <summary>
        /// The ALL APPLICATION PACKAGES SID (S-1-15-2-1).
        /// </summary>
        public static SecurityIdentifier AllApplicationPackages => new SecurityIdentifier("S-1-15-2-1");

        /// <summary>
        /// Creates a random SID that is unique to a single execution. It is added to the token as a
        /// restricting SID and granted access in the token's default DACL, which is what stops two
        /// concurrently sandboxed runs from reaching each other's kernel objects. It is also the principal
        /// granted write access to any directory the run is allowed to write to.
        /// </summary>
        /// <returns>A freshly generated, cryptographically random SID.</returns>
        public static SecurityIdentifier CreateUniqueRunSid()
        {
            var bytes = new byte[16];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }

            // S-1-5-21-a-b-c-d is the shape Windows uses for machine and domain accounts, and it is what
            // the Chromium sandbox generates for the same purpose. With 128 random bits a collision with a
            // real account is not a practical concern.
            var sid = string.Format(
                CultureInfo.InvariantCulture,
                "S-1-5-21-{0}-{1}-{2}-{3}",
                BitConverter.ToUInt32(bytes, 0),
                BitConverter.ToUInt32(bytes, 4),
                BitConverter.ToUInt32(bytes, 8),
                BitConverter.ToUInt32(bytes, 12));

            return new SecurityIdentifier(sid);
        }

        /// <summary>
        /// Copies a managed SID into its binary form.
        /// </summary>
        /// <param name="sid">The security identifier to convert.</param>
        /// <returns>The binary representation of the SID.</returns>
        public static byte[] ToBinary(SecurityIdentifier sid)
        {
            var bytes = new byte[sid.BinaryLength];
            sid.GetBinaryForm(bytes, 0);
            return bytes;
        }
    }
}
