// <copyright file="RestrictedProcess.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the Apache License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.Process
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;

    using global::RestrictedProcess.JobObjects;

    using Microsoft.Win32.SafeHandles;

    public class RestrictedProcess : IDisposable
    {
        private readonly SafeProcessHandle safeProcessHandle;
        private readonly string fileName = string.Empty;
        private readonly RestrictedProcessOptions options;
        private ProcessInformation processInformation;
        private JobObject? jobObject;
        private int exitCode;

        public RestrictedProcess(string fileName, string? workingDirectory, IEnumerable<string>? arguments = null, int bufferSize = 4096, Encoding? encoding = null, RestrictedProcessOptions? options = null)
        {
            // Initialize fields
            this.fileName = fileName;
            this.options = options ?? new RestrictedProcessOptions();
            this.IsDisposed = false;

            // Prepare startup info and redirect standard IO handles
            var startupInfo = new StartupInfo();
            this.RedirectStandardIoHandles(ref startupInfo, bufferSize, encoding ?? GetAnsiEncoding());

            // Create restricted token
            var restrictedToken = this.CreateRestrictedToken(this.options.TokenLevel);

            // Set mandatory label
            this.SetTokenMandatoryLabel(restrictedToken, (SecurityMandatoryLabel)this.options.IntegrityLevel);

            var processSecurityAttributes = new SecurityAttributes();
            var threadSecurityAttributes = new SecurityAttributes();
            this.processInformation = default(ProcessInformation);

            var creationFlags = (uint)(
                CreateProcessFlags.CREATE_SUSPENDED |
                CreateProcessFlags.CREATE_BREAKAWAY_FROM_JOB |
                CreateProcessFlags.CREATE_UNICODE_ENVIRONMENT |
                CreateProcessFlags.CREATE_NEW_PROCESS_GROUP |
                CreateProcessFlags.DETACHED_PROCESS | // http://stackoverflow.com/questions/6371149/what-is-the-difference-between-detach-process-and-create-no-window-process-creat
                CreateProcessFlags.CREATE_NO_WINDOW) |
                (uint)ProcessPriorityClass.High;

            string commandLine;
            if (arguments != null)
            {
                var commandLineBuilder = new StringBuilder();
                commandLineBuilder.AppendFormat("\"{0}\"", fileName);
                foreach (var argument in arguments)
                {
                    commandLineBuilder.Append(' ');
                    commandLineBuilder.Append(argument);
                }

                commandLine = commandLineBuilder.ToString();
            }
            else
            {
                commandLine = fileName;
            }

            ProcThreadAttributeList? attributeList = null;
            var environmentBlock = IntPtr.Zero;
            try
            {
                attributeList = this.CreateProcThreadAttributeList(startupInfo);
                if (attributeList != null)
                {
                    startupInfo.UseExtendedStartupInfo(attributeList.Pointer);
                    creationFlags |= (uint)CreateProcessFlags.EXTENDED_STARTUPINFO_PRESENT;
                }

                environmentBlock = this.CreateEnvironmentBlock(workingDirectory);

                if (!NativeMethods.CreateProcessAsUser(
                        restrictedToken,
                        null,
                        commandLine,
                        processSecurityAttributes,
                        threadSecurityAttributes,
                        true, // In order to standard input, output and error redirection work, the handles must be inheritable and the CreateProcess() API must specify that inheritable handles are to be inherited by the child process by specifying TRUE in the bInheritHandles parameter.
                        creationFlags,
                        environmentBlock,
                        workingDirectory,
                        startupInfo,
                        out this.processInformation))
                {
                    throw new Win32Exception();
                }
            }
            finally
            {
                // This is a very important line! Without disposing the startupInfo handles, reading the standard output (or error) will hang forever.
                // Same problem described here: http://social.msdn.microsoft.com/Forums/vstudio/en-US/3c25a2e8-b1ea-4fc4-927b-cb865d435147/how-does-processstart-work-in-getting-output
                startupInfo.Dispose();

                attributeList?.Dispose();
                if (environmentBlock != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(environmentBlock);
                }

                NativeMethods.CloseHandle(restrictedToken);
            }

            this.safeProcessHandle = new SafeProcessHandle(this.processInformation.Process);
        }

        public StreamWriter StandardInput { get; private set; } = null!;

        public StreamReader StandardOutput { get; private set; } = null!;

        public StreamReader StandardError { get; private set; } = null!;

        public int Id => this.processInformation.ProcessId;

        public int MainThreadId => this.processInformation.ThreadId;

        public IntPtr Handle => this.processInformation.Process;

        public IntPtr MainThreadHandle => this.processInformation.Thread;

        public bool HasExited
        {
            get
            {
                if (this.safeProcessHandle.IsInvalid || this.safeProcessHandle.IsClosed)
                {
                    return true;
                }

                return NativeMethods.GetExitCodeProcess(this.safeProcessHandle, out this.exitCode)
                       && this.exitCode != NativeMethods.STILL_ACTIVE;
            }
        }

        public int ExitCode
        {
            get
            {
                if (!this.HasExited)
                {
                    throw new InvalidOperationException("Process is still active!");
                }

                return this.exitCode;
            }
        }

        public string ExitCodeAsString => new Win32Exception(this.ExitCode).Message;

        /// <summary>
        /// Gets the time the process was started.
        /// </summary>
        public DateTime StartTime => this.GetProcessTimes().StartTime;

        /// <summary>
        /// Gets the time that the process exited.
        /// </summary>
        public DateTime ExitTime => this.GetProcessTimes().ExitTime;

        /// <summary>
        /// Gets the amount of time the process has spent running code inside the operating system core.
        /// </summary>
        public TimeSpan PrivilegedProcessorTime => this.GetProcessTimes().PrivilegedProcessorTime;

        /// <summary>
        /// Gets the amount of time the associated process has spent running code inside the application portion of the process (not the operating system core).
        /// </summary>
        public TimeSpan UserProcessorTime => this.GetProcessTimes().UserProcessorTime;

        /// <summary>
        /// Gets the amount of time the associated process has spent utilizing the CPU.
        /// </summary>
        public TimeSpan TotalProcessorTime => this.GetProcessTimes().TotalProcessorTime;

        /// <summary>
        /// Gets the name of the process.
        /// Warning: If two processes with the same name are created, this property may not return correct name!
        /// </summary>
        public string Name
        {
            get
            {
                var fileNameOnly = new FileInfo(this.fileName).Name;
                if (this.fileName.EndsWith(".exe"))
                {
                    return fileNameOnly.Substring(0, fileNameOnly.Length - 4);
                }

                return fileNameOnly;
            }
        }

        /// <summary>
        /// Gets the peak amount of memory (in bytes) committed by all processes ever associated with the job object.
        /// The job object tracks this value even after the process has exited, which makes it more reliable
        /// than sampling <see cref="PeakWorkingSetSize"/> for short-lived processes.
        /// </summary>
        public long PeakJobMemoryUsed =>
            this.jobObject == null
                ? 0
                : (long)(ulong)this.jobObject.GetExtendedLimitInformation().PeakJobMemoryUsed;

        public long PeakWorkingSetSize
        {
            get
            {
                var counters = default(ProcessMemoryCounters);
                NativeMethods.GetProcessMemoryInfo(this.Handle, out counters, (uint)Marshal.SizeOf(counters));
                return (int)counters.PeakWorkingSetSize;
            }
        }

        public long PeakPagefileUsage
        {
            get
            {
                var counters = default(ProcessMemoryCounters);
                NativeMethods.GetProcessMemoryInfo(this.Handle, out counters, (uint)Marshal.SizeOf(counters));
                return (int)counters.PeakPagefileUsage;
            }
        }

        public bool IsDisposed { get; private set; }

        public void Start(int timeLimit, int memoryLimit)
        {
            try
            {
                this.jobObject = new JobObject();
                this.jobObject.SetExtendedLimitInformation(PrepareJobObject.GetExtendedLimitInformation(timeLimit * 2, memoryLimit * 2));
                this.jobObject.SetBasicUiRestrictions(PrepareJobObject.GetUiRestrictions());
                if (!this.jobObject.AddProcess(this.processInformation.Process))
                {
                    throw new Win32Exception();
                }

                NativeMethods.ResumeThread(this.processInformation.Thread);
            }
            catch (Win32Exception)
            {
                this.Kill();
                throw;
            }
        }

        public void Kill()
        {
            NativeMethods.TerminateProcess(this.safeProcessHandle, -1);
        }

        public bool WaitForExit(int milliseconds)
        {
            var result = NativeMethods.WaitForSingleObject(this.processInformation.Process, (uint)milliseconds);
            return result != 258; // TODO: Extract as constant and check all cases (http://msdn.microsoft.com/en-us/library/windows/desktop/ms687032%28v=vs.85%29.aspx)
        }

        public void Dispose()
        {
            this.Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.IsDisposed = true;
                this.safeProcessHandle.Dispose();
                NativeMethods.CloseHandle(this.processInformation.Thread);
                this.jobObject?.Dispose();

                // Disposing these object causes "System.InvalidOperationException: The stream is currently in use by a previous operation on the stream."
                // this.StandardInput.Dispose();
                // this.StandardOutput.Dispose();
                // this.StandardError.Dispose();
            }
        }

        /// <summary>
        /// Gets the encoding matching the system's active ANSI code page, which is the encoding
        /// console child processes use for redirected standard IO by default.
        /// On .NET Framework this is what <see cref="Encoding.Default"/> returns, but on modern
        /// .NET <see cref="Encoding.Default"/> is always UTF-8, so the code page is resolved explicitly.
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

        private static IntPtr ConvertStringSidToSid(string stringSid, List<IntPtr> sidsToFree)
        {
            if (!NativeMethods.ConvertStringSidToSid(stringSid, out var sid))
            {
                throw new Win32Exception();
            }

            sidsToFree.Add(sid);
            return sid;
        }

        /// <summary>
        /// Gets the logon SID of the given token (the group carrying the SE_GROUP_LOGON_ID attribute).
        /// Returns the buffer holding the TOKEN_GROUPS structure the SID points into;
        /// the caller must free it with <see cref="Marshal.FreeHGlobal"/> after the SID is no longer needed.
        /// </summary>
        private static IntPtr GetLogonSid(IntPtr token, out IntPtr logonSid)
        {
            logonSid = IntPtr.Zero;
            NativeMethods.GetTokenInformation(token, TokenInformationClass.TokenGroups, IntPtr.Zero, 0, out var length);
            if (length == 0)
            {
                return IntPtr.Zero;
            }

            var buffer = Marshal.AllocHGlobal(length);
            if (!NativeMethods.GetTokenInformation(token, TokenInformationClass.TokenGroups, buffer, length, out length))
            {
                Marshal.FreeHGlobal(buffer);
                throw new Win32Exception();
            }

            // TOKEN_GROUPS layout: DWORD GroupCount (padded to pointer size), then SID_AND_ATTRIBUTES[GroupCount]
            var groupCount = Marshal.ReadInt32(buffer);
            var sizeOfEntry = Marshal.SizeOf<SidAndAttributes>();
            for (var i = 0; i < groupCount; i++)
            {
                var entry = Marshal.PtrToStructure<SidAndAttributes>(buffer + IntPtr.Size + (i * sizeOfEntry));
                if ((entry.Attributes & NativeMethods.SE_GROUP_LOGON_ID) == NativeMethods.SE_GROUP_LOGON_ID)
                {
                    logonSid = entry.Sid;
                    return buffer;
                }
            }

            Marshal.FreeHGlobal(buffer);
            return IntPtr.Zero;
        }

        /// <summary>
        /// Builds a double-null-terminated, case-insensitively sorted Unicode environment block
        /// for the child process, marshaled to unmanaged memory the caller must free.
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

        private static string GetEnvironmentVariableOrEmpty(string name)
        {
            return Environment.GetEnvironmentVariable(name) ?? string.Empty;
        }

        private void RedirectStandardIoHandles(ref StartupInfo startupInfo, int bufferSize, Encoding encoding)
        {
            // Some of this code is based on System.Diagnostics.Process.StartWithCreateProcess method implementation
            SafeFileHandle standardInputWritePipeHandle;
            SafeFileHandle standardOutputReadPipeHandle;
            SafeFileHandle standardErrorReadPipeHandle;

            // http://support.microsoft.com/kb/190351 (How to spawn console processes with redirected standard handles)
            // If the dwFlags member is set to STARTF_USESTDHANDLES, then the following STARTUPINFO members specify the standard handles of the child console based process:
            // HANDLE hStdInput - Standard input handle of the child process.
            // HANDLE hStdOutput - Standard output handle of the child process.
            // HANDLE hStdError - Standard error handle of the child process.
            startupInfo.Flags = (int)StartupInfoFlags.STARTF_USESTDHANDLES;
            this.CreatePipe(out standardInputWritePipeHandle, out startupInfo.StandardInputHandle, true, bufferSize);
            this.CreatePipe(out standardOutputReadPipeHandle, out startupInfo.StandardOutputHandle, false, bufferSize);
            this.CreatePipe(out standardErrorReadPipeHandle, out startupInfo.StandardErrorHandle, false, 4096);

            this.StandardInput = new StreamWriter(new FileStream(standardInputWritePipeHandle, FileAccess.Write, bufferSize, false), encoding, bufferSize)
                                     {
                                         AutoFlush = true,
                                     };
            this.StandardOutput = new StreamReader(new FileStream(standardOutputReadPipeHandle, FileAccess.Read, bufferSize, false), encoding, true, bufferSize);
            this.StandardError = new StreamReader(new FileStream(standardErrorReadPipeHandle, FileAccess.Read, 4096, false), encoding, true, 4096);

            /*
             * Child processes that use such C run-time functions as printf() and fprintf() can behave poorly when redirected.
             * The C run-time functions maintain separate IO buffers. When redirected, these buffers might not be flushed immediately after each IO call.
             * As a result, the output to the redirection pipe of a printf() call or the input from a getch() call is not flushed immediately and delays, sometimes-infinite delays occur.
             * This problem is avoided if the child process flushes the IO buffers after each call to a C run-time IO function.
             * Only the child process can flush its C run-time IO buffers. A process can flush its C run-time IO buffers by calling the fflush() function.
             */
        }

        private void CreatePipeWithSecurityAttributes(out SafeFileHandle readPipe, out SafeFileHandle writePipe, SecurityAttributes pipeAttributes, int size)
        {
            if (!NativeMethods.CreatePipe(out readPipe, out writePipe, pipeAttributes, size) || readPipe.IsInvalid
                || writePipe.IsInvalid)
            {
                throw new Win32Exception();
            }
        }

        // Using synchronous Anonymous pipes for process input/output redirection means we would end up
        // wasting a worker thread pool thread per pipe instance. Overlapped pipe IO is desirable, since
        // it will take advantage of the NT IO completion port infrastructure. But we can't really use
        // Overlapped I/O for process input/output as it would break Console apps (managed Console class
        // methods such as WriteLine as well as native CRT functions like printf) which are making an
        // assumption that the console standard handles (obtained via GetStdHandle()) are opened
        // for synchronous I/O and hence they can work fine with ReadFile/WriteFile synchronously!
        private void CreatePipe(out SafeFileHandle parentHandle, out SafeFileHandle childHandle, bool parentInputs, int bufferSize)
        {
            var securityAttributesParent = new SecurityAttributes { InheritHandle = true };

            SafeFileHandle? tempHandle = null;
            try
            {
                if (parentInputs)
                {
                    this.CreatePipeWithSecurityAttributes(out childHandle, out tempHandle, securityAttributesParent, bufferSize);
                }
                else
                {
                    this.CreatePipeWithSecurityAttributes(out tempHandle, out childHandle, securityAttributesParent, bufferSize);
                }

                // Duplicate the parent handle to be non-inheritable so that the child process
                // doesn't have access. This is done for correctness sake, exact reason is unclear.
                // One potential theory is that child process can do something brain dead like
                // closing the parent end of the pipe and there by getting into a blocking situation
                // as parent will not be draining the pipe at the other end anymore.

                // Create a duplicate of the output write handle for the std error write handle.
                // This is necessary in case the child application closes one of its std output handles.
                if (!NativeMethods.DuplicateHandle(
                        new HandleRef(this, NativeMethods.GetCurrentProcess()),
                        tempHandle,
                        new HandleRef(this, NativeMethods.GetCurrentProcess()),
                        out parentHandle, // Address of new handle.
                        0,
                        false, // Make it un-inheritable.
                        (int)DuplicateOptions.DUPLICATE_SAME_ACCESS))
                {
                    throw new Win32Exception();
                }
            }
            finally
            {
                // Close inheritable copies of the handles you do not want to be inherited.
                if (tempHandle != null && !tempHandle.IsInvalid)
                {
                    tempHandle.Close();
                }
            }
        }

        /// <summary>
        /// Builds the environment block passed to the child process, honoring
        /// <see cref="RestrictedProcessOptions.ScrubEnvironment"/> and
        /// <see cref="RestrictedProcessOptions.AdditionalEnvironmentVariables"/>.
        /// Returns <see cref="IntPtr.Zero"/> to inherit the parent's environment unchanged.
        /// </summary>
        private IntPtr CreateEnvironmentBlock(string? workingDirectory)
        {
            var additional = this.options.AdditionalEnvironmentVariables;

            if (!this.options.ScrubEnvironment)
            {
                if (additional.Count == 0)
                {
                    // Inherit the parent's environment unchanged (the historical behavior).
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

            // A minimal environment holding only what a console program typically needs to run.
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

            foreach (var pair in additional)
            {
                variables[pair.Key] = pair.Value;
            }

            return BuildEnvironmentBlock(variables);
        }

        /// <summary>
        /// Builds the PROC_THREAD_ATTRIBUTE_LIST carrying the enabled process creation hardening:
        /// the inheritable handle whitelist (only the three standard IO pipes), the mitigation
        /// policies and the child process creation ban. Returns null when nothing is enabled.
        /// </summary>
        private ProcThreadAttributeList? CreateProcThreadAttributeList(StartupInfo startupInfo)
        {
            var attributeCount = (this.options.RestrictInheritedHandles ? 1 : 0)
                                 + (this.options.Mitigations != ProcessMitigations.None ? 1 : 0)
                                 + (this.options.DisallowChildProcesses ? 1 : 0);
            if (attributeCount == 0)
            {
                return null;
            }

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

                if (this.options.Mitigations != ProcessMitigations.None)
                {
                    attributeList.SetMitigationPolicy((ulong)this.options.Mitigations);
                }

                if (this.options.DisallowChildProcesses)
                {
                    attributeList.SetChildProcessRestricted();
                }

                return attributeList;
            }
            catch
            {
                attributeList.Dispose();
                throw;
            }
        }

        private IntPtr CreateRestrictedToken(TokenLevel tokenLevel)
        {
            // Open the current process and grab its primary token
            IntPtr processToken;
            if (!NativeMethods.OpenProcessToken(
                    NativeMethods.GetCurrentProcess(),
                    NativeMethods.TOKEN_DUPLICATE | NativeMethods.TOKEN_ASSIGN_PRIMARY | NativeMethods.TOKEN_QUERY | NativeMethods.TOKEN_ADJUST_DEFAULT,
                    out processToken))
            {
                throw new Win32Exception();
            }

            var sidsToFree = new List<IntPtr>();
            var logonSidBuffer = IntPtr.Zero;
            try
            {
                // Remove all privileges except SeChangeNotifyPrivilege and convert the
                // Administrators group to deny-only, so a sandbox running in an elevated
                // host cannot use administrative rights.
                var flags = tokenLevel == TokenLevel.Unrestricted
                    ? default(CreateRestrictedTokenFlags)
                    : CreateRestrictedTokenFlags.DISABLE_MAX_PRIVILEGE;

                SidAndAttributes[]? sidsToDisable = null;
                if (tokenLevel >= TokenLevel.Limited)
                {
                    sidsToDisable = new[]
                    {
                        new SidAndAttributes { Sid = ConvertStringSidToSid(NativeMethods.SID_BUILTIN_ADMINISTRATORS, sidsToFree) },
                    };
                }

                // Restricting SIDs add a second access check evaluated only against this list,
                // mirroring the Chromium sandbox USER_LIMITED token level.
                SidAndAttributes[]? sidsToRestrict = null;
                if (tokenLevel >= TokenLevel.Restricted)
                {
                    var restrictedSids = new List<SidAndAttributes>
                    {
                        new SidAndAttributes { Sid = ConvertStringSidToSid(NativeMethods.SID_EVERYONE, sidsToFree) },
                        new SidAndAttributes { Sid = ConvertStringSidToSid(NativeMethods.SID_BUILTIN_USERS, sidsToFree) },
                        new SidAndAttributes { Sid = ConvertStringSidToSid(NativeMethods.SID_RESTRICTED, sidsToFree) },
                    };

                    logonSidBuffer = GetLogonSid(processToken, out var logonSid);
                    if (logonSid != IntPtr.Zero)
                    {
                        restrictedSids.Add(new SidAndAttributes { Sid = logonSid });
                    }

                    sidsToRestrict = restrictedSids.ToArray();
                }

                IntPtr restrictedToken;
                if (!NativeMethods.CreateRestrictedToken(
                        processToken,
                        flags,
                        sidsToDisable?.Length ?? 0,
                        sidsToDisable,
                        0, // Delete privilege (superseded by DISABLE_MAX_PRIVILEGE)
                        null,
                        sidsToRestrict?.Length ?? 0,
                        sidsToRestrict,
                        out restrictedToken))
                {
                    throw new Win32Exception();
                }

                return restrictedToken;
            }
            finally
            {
                foreach (var sid in sidsToFree)
                {
                    NativeMethods.LocalFree(sid);
                }

                if (logonSidBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(logonSidBuffer);
                }

                NativeMethods.CloseHandle(processToken);
            }
        }

        private void SetTokenMandatoryLabel(IntPtr token, SecurityMandatoryLabel securityMandatoryLabel)
        {
            // Create the low integrity SID.
            IntPtr integritySid;
            if (!NativeMethods.AllocateAndInitializeSid(
                    ref NativeMethods.SECURITY_MANDATORY_LABEL_AUTHORITY,
                    1,
                    (int)securityMandatoryLabel,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    out integritySid))
            {
                throw new Win32Exception();
            }

            var tokenInfo = IntPtr.Zero;
            try
            {
                var tokenMandatoryLabel = new TokenMandatoryLabel { Label = default(SidAndAttributes) };
                tokenMandatoryLabel.Label.Attributes = NativeMethods.SE_GROUP_INTEGRITY;
                tokenMandatoryLabel.Label.Sid = integritySid;
                //// Marshal the TOKEN_MANDATORY_LABEL structure to the native memory.
                var sizeOfTokenMandatoryLabel = Marshal.SizeOf(tokenMandatoryLabel);
                tokenInfo = Marshal.AllocHGlobal(sizeOfTokenMandatoryLabel);
                Marshal.StructureToPtr(tokenMandatoryLabel, tokenInfo, false);

                // Set the integrity level in the access token
                if (!NativeMethods.SetTokenInformation(
                        token,
                        TokenInformationClass.TokenIntegrityLevel,
                        tokenInfo,
                        sizeOfTokenMandatoryLabel + NativeMethods.GetLengthSid(integritySid)))
                {
                    throw new Win32Exception();
                }
            }
            finally
            {
                if (tokenInfo != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(tokenInfo);
                }

                NativeMethods.FreeSid(integritySid);
            }
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
                throw new Win32Exception();
            }

            return processTimes;
        }
    }
}
