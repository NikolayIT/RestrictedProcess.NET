// <copyright file="RestrictedProcess.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.Process
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Security.Principal;
    using System.Text;
    using System.Threading;

    using global::RestrictedProcess.JobObjects;

    using Microsoft.Win32.SafeHandles;

    /// <summary>
    /// A manual reimplementation of Process.Start that puts the new process behind every sandbox boundary
    /// Windows offers to an unelevated caller: a restricted token at a low integrity level, a job object
    /// attached at creation, process creation mitigation policies, an inherited-handle whitelist, a
    /// kernel-level child-process ban, a scrubbed environment, a throwaway desktop and, optionally, an
    /// AppContainer with no network capability.
    /// </summary>
    public sealed class RestrictedProcess : IDisposable
    {
        private readonly RestrictedProcessOptions options;
        private readonly string fileName;
        private readonly JobObject jobObject;
        private readonly JobNotificationListener notifications;
        private readonly SafeProcessHandle safeProcessHandle;
        private readonly SandboxDesktop? desktop;
        private readonly AppContainerProfile? appContainer;
        private readonly WritableDirectoryGrant? writableDirectories;
        private readonly SecurityIdentifier uniqueRunSid;

        private ProcessInformation processInformation;
        private IntPtr mainThreadHandle;
        private long peakWorkingSetBytes;
        private int killed;
        private int disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="RestrictedProcess"/> class and creates the
        /// sandboxed process in a suspended state. Call <see cref="Start"/> to let it run.
        /// </summary>
        /// <param name="startInfo">What to run and the limits to enforce.</param>
        /// <param name="options">The sandbox configuration.</param>
        public RestrictedProcess(RestrictedProcessStartInfo startInfo, RestrictedProcessOptions options)
        {
            if (startInfo == null)
            {
                throw new ArgumentNullException(nameof(startInfo));
            }

            this.options = options ?? throw new ArgumentNullException(nameof(options));
            ValidateOptions(this.options);
            this.fileName = Path.GetFullPath(startInfo.FileName);
            this.uniqueRunSid = SidFactory.CreateUniqueRunSid();

            var encoding = startInfo.Encoding ?? GetAnsiEncoding();
            var startupInfo = new StartupInfo();
            SandboxToken? token = null;
            ProcThreadAttributeList? attributeList = null;
            var environmentBlock = IntPtr.Zero;
            var assignToJobAfterCreation = false;

            try
            {
                this.RedirectStandardIoHandles(ref startupInfo, startInfo.PipeBufferSize, encoding);

                token = RestrictedTokenBuilder.Create(this.options, this.uniqueRunSid);

                if (this.options.WritableDirectories.Count > 0)
                {
                    this.writableDirectories = new WritableDirectoryGrant(this.uniqueRunSid, this.options.WritableDirectories);
                }

                if (this.options.BlockNetworkAccess)
                {
                    this.appContainer = new AppContainerProfile(
                        this.options.AppContainerProfileName,
                        new[] { this.fileName, startInfo.WorkingDirectory ?? string.Empty }.Where(x => x.Length > 0));
                }

                if (this.options.UseAlternateDesktop)
                {
                    this.desktop = new SandboxDesktop(
                        this.options.IntegrityLevel,
                        token.UserSid,
                        this.BuildDesktopAllowedSids(token),
                        this.options.UseAlternateWindowStation);
                    startupInfo.Desktop = Marshal.StringToHGlobalUni(this.desktop.Name);
                }

                // The job is created before the process so it can be attached at creation time. Its limits
                // are then in force from the first instruction the process executes.
                this.jobObject = new JobObject();
                this.ConfigureJob(startInfo);
                this.notifications = new JobNotificationListener(this.jobObject);

                attributeList = this.CreateProcThreadAttributeList(startupInfo, out assignToJobAfterCreation);
                startupInfo.UseExtendedStartupInfo(attributeList.Pointer);

                environmentBlock = this.CreateEnvironmentBlock(startInfo.WorkingDirectory);

                var creationFlags = (uint)(CreateProcessFlags.CREATE_SUSPENDED
                                           | CreateProcessFlags.CREATE_UNICODE_ENVIRONMENT
                                           | CreateProcessFlags.DETACHED_PROCESS
                                           | CreateProcessFlags.EXTENDED_STARTUPINFO_PRESENT)
                                    | (uint)this.options.PriorityClass;

                var commandLine = new StringBuilder(CommandLine.Build(this.fileName, startInfo.Arguments));

                if (!NativeMethods.CreateProcessAsUser(
                        token.Handle,
                        this.fileName,
                        commandLine,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        true, // Required for the redirected standard IO handles; the handle list above is what makes it safe.
                        creationFlags,
                        environmentBlock,
                        startInfo.WorkingDirectory,
                        startupInfo,
                        out this.processInformation))
                {
                    throw SandboxException.FromLastWin32Error(SandboxStep.CreateProcess, this.fileName);
                }

                this.safeProcessHandle = new SafeProcessHandle(this.processInformation.Process);
                this.mainThreadHandle = this.processInformation.Thread;
                this.ExitedHandle = new ProcessWaitHandle(this.safeProcessHandle);
            }
            catch
            {
                this.jobObject?.Dispose();
                this.notifications?.Dispose();
                this.desktop?.Dispose();
                this.appContainer?.Dispose();
                this.writableDirectories?.Dispose();
                throw;
            }
            finally
            {
                // Critical: the child ends of the pipes have to be closed as soon as CreateProcessAsUser
                // returns. While the parent still holds a copy of the write end, reading standard output
                // never reaches end of file and the read hangs forever.
                if (startupInfo.Desktop != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(startupInfo.Desktop);
                    startupInfo.Desktop = IntPtr.Zero;
                }

                startupInfo.Dispose();

                // The attribute list holds unmanaged copies of every attribute value, and
                // UpdateProcThreadAttribute stored pointers to them, so it can only be freed now that
                // CreateProcessAsUser has returned.
                attributeList?.Dispose();

                if (environmentBlock != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(environmentBlock);
                }

                token?.Dispose();
            }

            if (assignToJobAfterCreation && !this.jobObject.AddProcess(this.processInformation.Process))
            {
                var failure = SandboxException.FromLastWin32Error(SandboxStep.AssignProcessToJob);
                this.Kill();
                throw failure;
            }
        }

        /// <summary>
        /// Gets the writer for the standard input of the sandboxed process.
        /// </summary>
        public StreamWriter StandardInput { get; private set; } = null!;

        /// <summary>
        /// Gets the reader for the standard output of the sandboxed process.
        /// </summary>
        public StreamReader StandardOutput { get; private set; } = null!;

        /// <summary>
        /// Gets the reader for the standard error of the sandboxed process.
        /// </summary>
        public StreamReader StandardError { get; private set; } = null!;

        /// <summary>
        /// Gets the identifier of the sandboxed process.
        /// </summary>
        public int Id => this.processInformation.ProcessId;

        /// <summary>
        /// Gets the SID generated for this execution alone. It is a restricting SID on the token and is
        /// granted access in the token's default DACL, so objects this process creates are not reachable
        /// by other sandboxed runs of the same user.
        /// </summary>
        public SecurityIdentifier UniqueRunSid => this.uniqueRunSid;

        /// <summary>
        /// Gets the name of the desktop the process runs on, or null when the alternate desktop is off.
        /// </summary>
        public string? DesktopName => this.desktop?.Name;

        /// <summary>
        /// Gets a value indicating whether the process has exited.
        /// </summary>
        public bool HasExited
        {
            get
            {
                if (this.safeProcessHandle.IsInvalid || this.safeProcessHandle.IsClosed)
                {
                    return true;
                }

                // Deliberately not GetExitCodeProcess against STILL_ACTIVE: a program that legitimately
                // exits with code 259 would be reported as running forever.
                return NativeMethods.WaitForSingleObject(this.safeProcessHandle, 0) == NativeMethods.WAIT_OBJECT_0;
            }
        }

        /// <summary>
        /// Gets the exit code of the process.
        /// </summary>
        public int ExitCode
        {
            get
            {
                if (!NativeMethods.GetExitCodeProcess(this.safeProcessHandle, out var exitCode))
                {
                    throw new InvalidOperationException(
                        "Could not read the exit code of the sandboxed process.",
                        SandboxException.FromLastWin32Error(SandboxStep.QueryProcessTimes, "GetExitCodeProcess"));
                }

                return exitCode;
            }
        }

        /// <summary>
        /// Gets a wait handle that is signalled when the root process exits.
        /// </summary>
        public WaitHandle ExitedHandle { get; private set; } = null!;

        /// <summary>
        /// Gets a wait handle that is signalled when every process in the job has exited.
        /// </summary>
        public WaitHandle AllProcessesExited => this.notifications.AllProcessesExited;

        /// <summary>
        /// Gets a value indicating whether the job reported that a soft memory or processor time limit was
        /// exceeded while the process was running.
        /// </summary>
        public bool NotificationLimitExceeded => this.notifications.NotificationLimitExceeded;

        /// <summary>
        /// Gets a wait handle signalled as soon as the job reports that a soft limit was crossed, so a
        /// program that goes over can be stopped immediately instead of running out its deadline.
        /// </summary>
        public WaitHandle NotificationLimitReached => this.notifications.NotificationLimitReached;

        /// <summary>
        /// Gets a value indicating whether the job reported that the disk write limit specifically was the
        /// limit that was crossed. Read it before killing the process: the job keeps the violation record,
        /// but there is no reason to make the caller guess which threshold fired.
        /// </summary>
        public bool DiskWriteLimitExceeded
        {
            get
            {
                if (!this.notifications.NotificationLimitExceeded)
                {
                    return false;
                }

                var violation = this.jobObject.GetLimitViolationInformation();
                return (violation.ViolationLimitFlags & (uint)LimitFlags.JOB_OBJECT_LIMIT_JOB_WRITE_BYTES) != 0;
            }
        }

        /// <summary>
        /// Gets the wall clock time between process creation and exit, computed from the raw file times so
        /// it is unaffected by the local time zone or a daylight saving transition mid-run.
        /// </summary>
        public TimeSpan WallClockTime
        {
            get
            {
                var times = this.GetProcessTimes();
                return times.Exit <= times.Create ? TimeSpan.Zero : TimeSpan.FromTicks(times.Exit - times.Create);
            }
        }

        /// <summary>
        /// Gets the user-mode processor time accumulated by every process in the job, including ones that
        /// have already exited.
        /// </summary>
        public TimeSpan UserProcessorTime => TimeSpan.FromTicks(this.jobObject.GetAccountingInformation().BasicInfo.TotalUserTime);

        /// <summary>
        /// Gets the kernel-mode processor time accumulated by every process in the job.
        /// </summary>
        public TimeSpan PrivilegedProcessorTime => TimeSpan.FromTicks(this.jobObject.GetAccountingInformation().BasicInfo.TotalKernelTime);

        /// <summary>
        /// Gets the total processor time accumulated by every process in the job.
        /// </summary>
        public TimeSpan TotalProcessorTime
        {
            get
            {
                var accounting = this.jobObject.GetAccountingInformation().BasicInfo;
                return TimeSpan.FromTicks(accounting.TotalUserTime + accounting.TotalKernelTime);
            }
        }

        /// <summary>
        /// Gets the peak memory committed by all processes ever associated with the job. The job keeps
        /// this figure after the processes are gone, which makes it the dependable memory metric.
        /// </summary>
        public long PeakCommitBytes => (long)(ulong)this.jobObject.GetExtendedLimitInformation().PeakJobMemoryUsed;

        /// <summary>
        /// Gets the highest peak working set observed for the root process. Working set is physical
        /// residency, so it depends on system memory pressure and is not reproducible between machines;
        /// <see cref="PeakCommitBytes"/> is the better metric for judging.
        /// </summary>
        public long PeakWorkingSetBytes => Interlocked.Read(ref this.peakWorkingSetBytes);

        /// <summary>
        /// Gets the I/O accumulated by every process in the job.
        /// </summary>
        public ProcessIoStatistics IoStatistics
        {
            get
            {
                var io = this.jobObject.GetAccountingInformation().IoInfo;
                return new ProcessIoStatistics(io.ReadOperationCount, io.WriteOperationCount, io.ReadTransferCount, io.WriteTransferCount);
            }
        }

        /// <summary>
        /// Reads the current peak working set of the root process and folds it into
        /// <see cref="PeakWorkingSetBytes"/>. Cheap enough to call at the interesting moments (before a
        /// kill, right after exit) instead of on a timer.
        /// </summary>
        public void SampleWorkingSet()
        {
            if (this.safeProcessHandle.IsInvalid || this.safeProcessHandle.IsClosed)
            {
                return;
            }

            var counters = default(ProcessMemoryCounters);
            if (!NativeMethods.GetProcessMemoryInfo(this.safeProcessHandle, out counters, (uint)Marshal.SizeOf(counters)))
            {
                return;
            }

            var peak = (long)counters.PeakWorkingSetSize;
            long current;
            while ((current = Interlocked.Read(ref this.peakWorkingSetBytes)) < peak)
            {
                if (Interlocked.CompareExchange(ref this.peakWorkingSetBytes, peak, current) == current)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Resumes the suspended process.
        /// </summary>
        public void Start()
        {
            if (NativeMethods.ResumeThread(this.mainThreadHandle) == unchecked((uint)-1))
            {
                var failure = SandboxException.FromLastWin32Error(SandboxStep.ResumeThread);
                this.Kill();
                throw failure;
            }
        }

        /// <summary>
        /// Terminates the whole job. Safe to call from any thread, more than once, and after
        /// <see cref="Dispose"/> - the executor kills from I/O continuations that can outlive the run.
        /// </summary>
        public void Kill()
        {
            if (Interlocked.Exchange(ref this.killed, 1) != 0)
            {
                return;
            }

            this.SampleWorkingSet();

            // Terminating the job takes the whole tree, including anything that slipped past the active
            // process limit. The direct TerminateProcess is a fallback for the case where the process was
            // never successfully assigned to the job.
            this.jobObject.Terminate(unchecked((uint)-1));

            if (!this.safeProcessHandle.IsInvalid && !this.safeProcessHandle.IsClosed)
            {
                NativeMethods.TerminateProcess(this.safeProcessHandle, -1);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref this.disposed, 1) != 0)
            {
                return;
            }

            this.notifications.Dispose();

            if (this.mainThreadHandle != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(this.mainThreadHandle);
                this.mainThreadHandle = IntPtr.Zero;
            }

            this.ExitedHandle?.Dispose();
            this.safeProcessHandle?.Dispose();

            // Disposing the job kills anything still inside it (JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE).
            this.jobObject.Dispose();
            this.desktop?.Dispose();
            this.appContainer?.Dispose();
            this.writableDirectories?.Dispose();

            // The standard IO streams are intentionally left alone: closing them here throws
            // InvalidOperationException when an asynchronous read is still in flight, and the underlying
            // handles are released by their finalizers once the readers finish.
        }

        /// <summary>
        /// Rejects option combinations that cannot work, rather than letting them fail later as an
        /// unexplained start-up error.
        /// </summary>
        private static void ValidateOptions(RestrictedProcessOptions options)
        {
            if (options.BlockNetworkAccess && options.UseAlternateDesktop)
            {
                // An AppContainer process cannot attach to a desktop this library creates. It has been
                // tested against every security descriptor the desktop can be given - any DACL, any
                // mandatory label, on the current window station and on a private one - and the child
                // always dies during user32 initialisation with ERROR_DLL_INIT_FAILED. Only the desktop
                // the parent is already attached to works. Rather than hand back a process that cannot
                // start, say so, and let the caller decide which boundary matters more for the workload.
                var detail = "BlockNetworkAccess cannot be combined with UseAlternateDesktop: a process "
                             + "running in an AppContainer cannot attach to a desktop created by the "
                             + "sandbox, and fails to start. Set UseAlternateDesktop to false to keep the "
                             + "network block (the job object still denies clipboard access, global atoms "
                             + "and USER handles from outside the job), or set BlockNetworkAccess to false "
                             + "to keep the throwaway desktop.";
                throw SandboxException.For(SandboxStep.CreateDesktop, detail);
            }
        }

        /// <summary>
        /// Gets the encoding matching the system's active ANSI code page, which is what console child
        /// processes write to a redirected standard output by default. On modern .NET
        /// <see cref="Encoding.Default"/> is always UTF-8, so the code page is resolved explicitly.
        /// </summary>
        private static Encoding GetAnsiEncoding()
        {
            try
            {
                var ansiCodePage = (int)NativeMethods.GetACP();
                return CodePagesEncodingProvider.Instance.GetEncoding(ansiCodePage)
                       ?? Encoding.GetEncoding(ansiCodePage);
            }
            catch (NotSupportedException)
            {
                return Encoding.Default;
            }
            catch (ArgumentException)
            {
                return Encoding.Default;
            }
        }

        private static string GetEnvironmentVariableOrEmpty(string name)
        {
            return Environment.GetEnvironmentVariable(name) ?? string.Empty;
        }

        /// <summary>
        /// Builds a double-null-terminated, case-insensitively sorted Unicode environment block.
        /// </summary>
        private static IntPtr BuildEnvironmentBlock(IDictionary<string, string> variables)
        {
            var builder = new StringBuilder();
            foreach (var pair in variables.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                builder.Append(pair.Key).Append('=').Append(pair.Value).Append('\0');
            }

            builder.Append('\0');
            return Marshal.StringToHGlobalUni(builder.ToString());
        }

        private IReadOnlyList<SecurityIdentifier> BuildDesktopAllowedSids(SandboxToken token)
        {
            // The logon SID is the one group that stays enabled at every token level, so it is what the
            // first access check matches; the unique run SID is in the restricting list, so it is what the
            // second check matches. Both have to be present, and both are taken from the token that was
            // actually built rather than re-derived, so the DACL can never disagree with the token.
            var sids = new List<SecurityIdentifier>
            {
                this.uniqueRunSid,
                token.UserSid,
            };

            if (token.LogonSid != null)
            {
                sids.Add(token.LogonSid);
            }

            if (this.options.BlockNetworkAccess)
            {
                // An AppContainer process presents its package SID, and ALL APPLICATION PACKAGES unless it
                // is a Less Privileged AppContainer.
                if (this.appContainer != null)
                {
                    sids.Add(this.appContainer.SecurityIdentifier);
                }

                if (!this.options.UseLowPrivilegeAppContainer)
                {
                    sids.Add(SidFactory.AllApplicationPackages);
                }
            }

            return sids;
        }

        private void ConfigureJob(RestrictedProcessStartInfo startInfo)
        {
            var multiplier = Math.Max(1.0, this.options.JobLimitsMultiplier);
            var hardMemoryLimit = startInfo.MemoryLimitBytes.HasValue
                ? (long)Math.Min(long.MaxValue, startInfo.MemoryLimitBytes.Value * multiplier)
                : 0;

            this.jobObject.SetExtendedLimitInformation(
                PrepareJobObject.GetExtendedLimitInformation(this.options, hardMemoryLimit));
            this.jobObject.SetBasicUiRestrictions(PrepareJobObject.GetUiRestrictions());

            if (this.options.CpuRateLimitPercent.HasValue)
            {
                var percent = Math.Min(100, Math.Max(1, this.options.CpuRateLimitPercent.Value));
                this.jobObject.SetCpuRateControlInformation(new CpuRateControlInformation
                {
                    ControlFlags = CpuRateControlInformation.FlagEnable | CpuRateControlInformation.FlagHardCap,
                    CpuRate = (uint)(percent * 100),
                });
            }

            var notificationLimits = PrepareJobObject.GetNotificationLimits(
                startInfo.MemoryLimitBytes, startInfo.CpuTimeLimit, this.options.MaxDiskWriteBytes);
            if (notificationLimits.HasValue)
            {
                this.jobObject.TrySetNotificationLimits(notificationLimits.Value);
            }
        }

        private ProcThreadAttributeList CreateProcThreadAttributeList(StartupInfo startupInfo, out bool assignToJobAfterCreation)
        {
            assignToJobAfterCreation = false;

            var attributeCount = 2 // handle list and job list, both always used
                                 + ((this.options.Mitigations != ProcessMitigations.None
                                     || this.options.Mitigations2 != ProcessMitigations2.None) ? 1 : 0)
                                 + (this.options.DisallowChildProcesses ? 2 : 0)
                                 + (this.options.BlockNetworkAccess ? 1 : 0)
                                 + (this.options.BlockNetworkAccess && this.options.UseLowPrivilegeAppContainer ? 1 : 0);

            var attributeList = new ProcThreadAttributeList(attributeCount);
            try
            {
                if (this.options.RestrictInheritedHandles)
                {
                    attributeList.SetHandleList(
                        new[]
                        {
                            startupInfo.StandardInputHandle!.DangerousGetHandle(),
                            startupInfo.StandardOutputHandle!.DangerousGetHandle(),
                            startupInfo.StandardErrorHandle!.DangerousGetHandle(),
                        });
                }

                try
                {
                    attributeList.SetJobList(new[] { this.jobObject.Handle.DangerousGetHandle() });
                }
                catch (SandboxException)
                {
                    // Very old builds do not support attaching a job at creation. Falling back to
                    // AssignProcessToJobObject on the suspended process is still safe, just less tidy.
                    assignToJobAfterCreation = true;
                }

                if (this.options.Mitigations != ProcessMitigations.None
                    || this.options.Mitigations2 != ProcessMitigations2.None)
                {
                    attributeList.SetMitigationPolicy((ulong)this.options.Mitigations, (ulong)this.options.Mitigations2);
                }

                if (this.options.DisallowChildProcesses)
                {
                    attributeList.SetChildProcessRestricted();
                    attributeList.SetDesktopAppBreakawayDisabled();
                }

                if (this.options.BlockNetworkAccess && this.appContainer != null)
                {
                    attributeList.SetSecurityCapabilities(this.appContainer.Sid);

                    if (this.options.UseLowPrivilegeAppContainer)
                    {
                        attributeList.SetLowPrivilegeAppContainer();
                    }
                }

                return attributeList;
            }
            catch
            {
                attributeList.Dispose();
                throw;
            }
        }

        private void RedirectStandardIoHandles(ref StartupInfo startupInfo, int bufferSize, Encoding encoding)
        {
            startupInfo.Flags = (int)StartupInfoFlags.STARTF_USESTDHANDLES;
            this.CreatePipe(out var standardInputWrite, out startupInfo.StandardInputHandle, true, bufferSize);
            this.CreatePipe(out var standardOutputRead, out startupInfo.StandardOutputHandle, false, bufferSize);
            this.CreatePipe(out var standardErrorRead, out startupInfo.StandardErrorHandle, false, bufferSize);

            this.StandardInput = new StreamWriter(
                new FileStream(standardInputWrite, FileAccess.Write, bufferSize, false), encoding, bufferSize)
            {
                AutoFlush = true,
            };
            this.StandardOutput = new StreamReader(
                new FileStream(standardOutputRead, FileAccess.Read, bufferSize, false), encoding, true, bufferSize);
            this.StandardError = new StreamReader(
                new FileStream(standardErrorRead, FileAccess.Read, bufferSize, false), encoding, true, bufferSize);
        }

        private void CreatePipe(out SafeFileHandle parentHandle, out SafeFileHandle childHandle, bool parentInputs, int bufferSize)
        {
            var attributes = SecurityAttributes.Create(inheritHandle: true);

            SafeFileHandle? tempHandle = null;
            try
            {
                if (parentInputs)
                {
                    if (!NativeMethods.CreatePipe(out childHandle, out tempHandle, ref attributes, bufferSize))
                    {
                        throw SandboxException.FromLastWin32Error(SandboxStep.CreatePipe);
                    }
                }
                else
                {
                    if (!NativeMethods.CreatePipe(out tempHandle, out childHandle, ref attributes, bufferSize))
                    {
                        throw SandboxException.FromLastWin32Error(SandboxStep.CreatePipe);
                    }
                }

                // Duplicate the parent end as non-inheritable so the child never gets a copy: if it closed
                // the parent's end of its own output pipe the parent would block reading a pipe nobody can
                // write to.
                if (!NativeMethods.DuplicateHandle(
                        new HandleRef(this, NativeMethods.GetCurrentProcess()),
                        tempHandle,
                        new HandleRef(this, NativeMethods.GetCurrentProcess()),
                        out parentHandle,
                        0,
                        false,
                        (int)DuplicateOptions.DUPLICATE_SAME_ACCESS))
                {
                    throw SandboxException.FromLastWin32Error(SandboxStep.DuplicateHandle);
                }
            }
            finally
            {
                tempHandle?.Dispose();
            }
        }

        /// <summary>
        /// Builds the environment block for the child, honouring
        /// <see cref="RestrictedProcessOptions.ScrubEnvironment"/> and
        /// <see cref="RestrictedProcessOptions.AdditionalEnvironmentVariables"/>. Returns
        /// <see cref="IntPtr.Zero"/> to inherit the parent's environment unchanged.
        /// </summary>
        private IntPtr CreateEnvironmentBlock(string? workingDirectory)
        {
            var additional = this.options.AdditionalEnvironmentVariables;

            if (!this.options.ScrubEnvironment)
            {
                if (additional.Count == 0)
                {
                    return IntPtr.Zero;
                }

                var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
                {
                    merged[(string)entry.Key] = (string?)entry.Value ?? string.Empty;
                }

                foreach (var pair in additional)
                {
                    merged[pair.Key] = pair.Value;
                }

                return BuildEnvironmentBlock(merged);
            }

            var systemRoot = GetEnvironmentVariableOrEmpty("SystemRoot");
            var temp = string.IsNullOrEmpty(workingDirectory) ? GetEnvironmentVariableOrEmpty("TEMP") : workingDirectory!;
            var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["SystemRoot"] = systemRoot,
                ["SystemDrive"] = GetEnvironmentVariableOrEmpty("SystemDrive"),
                ["windir"] = GetEnvironmentVariableOrEmpty("windir"),
                ["ComSpec"] = GetEnvironmentVariableOrEmpty("ComSpec"),
                ["PATH"] = systemRoot + @"\system32;" + systemRoot,
                ["PATHEXT"] = ".COM;.EXE;.BAT;.CMD",
                ["TEMP"] = temp,
                ["TMP"] = temp,
                ["NUMBER_OF_PROCESSORS"] = GetEnvironmentVariableOrEmpty("NUMBER_OF_PROCESSORS"),
                ["PROCESSOR_ARCHITECTURE"] = GetEnvironmentVariableOrEmpty("PROCESSOR_ARCHITECTURE"),
                ["OS"] = "Windows_NT",
            };

            if (this.options.BlockNetworkAccess)
            {
                // Creating an AppContainer process fails with ERROR_ENVVAR_NOT_FOUND unless these profile
                // variables, which the container uses to resolve its private storage, are present.
                foreach (var name in new[] { "USERPROFILE", "APPDATA", "LOCALAPPDATA", "ALLUSERSPROFILE", "ProgramData", "HOMEDRIVE", "HOMEPATH", "USERNAME" })
                {
                    variables[name] = GetEnvironmentVariableOrEmpty(name);
                }
            }

            foreach (var pair in additional)
            {
                variables[pair.Key] = pair.Value;
            }

            return BuildEnvironmentBlock(variables);
        }

        private ProcessThreadTimes GetProcessTimes()
        {
            var processTimes = default(ProcessThreadTimes);
            if (!NativeMethods.GetProcessTimes(
                    this.safeProcessHandle,
                    out processTimes.Create,
                    out processTimes.Exit,
                    out processTimes.Kernel,
                    out processTimes.User))
            {
                throw SandboxException.FromLastWin32Error(SandboxStep.QueryProcessTimes);
            }

            return processTimes;
        }
    }
}
