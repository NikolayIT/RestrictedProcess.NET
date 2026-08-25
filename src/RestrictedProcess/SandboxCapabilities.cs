// <copyright file="SandboxCapabilities.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess
{
    using System;
    using System.Globalization;
    using System.Security.Principal;
    using System.Text;

    /// <summary>
    /// What the sandbox can actually do on this machine, probed once at startup.
    /// <para>
    /// Several boundaries depend on the host rather than on the code: network blocking needs the Windows
    /// Firewall service running, the second word of mitigation policies needs Windows 10 1703, and a
    /// hosted CI runner may not allow a private desktop at all. Discovering that through a failed
    /// execution is expensive and confusing; a caller can ask up front instead, and a test suite can skip
    /// rather than fail.
    /// </para>
    /// </summary>
    public sealed class SandboxCapabilities
    {
        private SandboxCapabilities()
        {
        }

        /// <summary>
        /// Gets the Windows version the probe ran on.
        /// </summary>
        public Version OperatingSystemVersion { get; private set; } = new Version(0, 0);

        /// <summary>
        /// Gets a value indicating whether the process can create a private desktop. Without it,
        /// <see cref="RestrictedProcessOptions.UseAlternateDesktop"/> has to be turned off.
        /// </summary>
        public bool CanCreateDesktop { get; private set; }

        /// <summary>
        /// Gets a value indicating whether a job object can be created and given soft notification limits.
        /// Without it the executor falls back to comparing the accounting totals after the run, which still
        /// works but cannot stop an over-limit program early.
        /// </summary>
        public bool SupportsJobNotificationLimits { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the second word of process creation mitigation policies is
        /// available (Windows 10 1703 and later).
        /// </summary>
        public bool SupportsExtendedMitigations { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the Windows Firewall service is running.
        /// <see cref="RestrictedProcessOptions.BlockNetworkAccess"/> relies on it: an AppContainer without
        /// network capabilities is only denied its sockets because the firewall enforces it, so with the
        /// service stopped the option provides no network boundary at all.
        /// </summary>
        public bool FirewallRunning { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the caller is running elevated. Not required, but worth
        /// reporting: the sandbox drops administrative rights from the token either way, and a run started
        /// from an elevated host is a good thing to have logged.
        /// </summary>
        public bool IsElevated { get; private set; }

        /// <summary>
        /// Probes the current machine.
        /// </summary>
        /// <returns>What this host supports.</returns>
        public static SandboxCapabilities Probe()
        {
            var capabilities = new SandboxCapabilities
            {
                OperatingSystemVersion = Environment.OSVersion.Version,
                SupportsExtendedMitigations = Environment.OSVersion.Version >= new Version(10, 0, 15063),
                IsElevated = ProbeElevation(),
                FirewallRunning = ProbeFirewall(),
            };

            capabilities.CanCreateDesktop = ProbeDesktop();
            capabilities.SupportsJobNotificationLimits = ProbeJobNotificationLimits();
            return capabilities;
        }

        /// <summary>
        /// Renders the probe result as a single line, suitable for a log or a skipped-test message.
        /// </summary>
        /// <returns>A human readable summary.</returns>
        public override string ToString()
        {
            var builder = new StringBuilder();
            builder.AppendFormat(CultureInfo.InvariantCulture, "Windows {0}", this.OperatingSystemVersion);
            builder.Append(", desktop=").Append(this.CanCreateDesktop);
            builder.Append(", jobNotifications=").Append(this.SupportsJobNotificationLimits);
            builder.Append(", extendedMitigations=").Append(this.SupportsExtendedMitigations);
            builder.Append(", firewall=").Append(this.FirewallRunning);
            builder.Append(", elevated=").Append(this.IsElevated);
            return builder.ToString();
        }

        private static bool ProbeElevation()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static bool ProbeFirewall()
        {
            var manager = Process.NativeMethods.OpenSCManager(null, null, Process.NativeMethods.SC_MANAGER_CONNECT);
            if (manager == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                var service = Process.NativeMethods.OpenService(manager, "MpsSvc", Process.NativeMethods.SERVICE_QUERY_STATUS);
                if (service == IntPtr.Zero)
                {
                    return false;
                }

                try
                {
                    return Process.NativeMethods.QueryServiceStatus(service, out var status)
                           && status.CurrentState == Process.NativeMethods.SERVICE_RUNNING;
                }
                finally
                {
                    Process.NativeMethods.CloseServiceHandle(service);
                }
            }
            finally
            {
                Process.NativeMethods.CloseServiceHandle(manager);
            }
        }

        private static bool ProbeDesktop()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    var user = identity.User;
                    if (user == null)
                    {
                        return false;
                    }

                    using (new Process.SandboxDesktop(IntegrityLevel.Low, user, new[] { user }, false))
                    {
                        return true;
                    }
                }
            }
            catch (SandboxException)
            {
                return false;
            }
        }

        private static bool ProbeJobNotificationLimits()
        {
            try
            {
                using (var job = new JobObjects.JobObject())
                {
                    var limits = JobObjects.PrepareJobObject.GetNotificationLimits(
                        1024L * 1024L * 1024L, TimeSpan.FromSeconds(1), null);
                    return limits.HasValue && job.TrySetNotificationLimits(limits.Value);
                }
            }
            catch (SandboxException)
            {
                return false;
            }
        }
    }
}
