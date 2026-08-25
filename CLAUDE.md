# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A Windows-only .NET library for running untrusted executables in a sandbox with time and memory limits
(originally extracted from the Open Judge System). It targets `net8.0-windows` and `net10.0-windows`; the
core is Win32 P/Invoke (`CreateProcessAsUser`, restricted tokens, job objects), so there is no
cross-platform path. `netstandard2.0` was dropped in the 2.0 rework, which also means .NET Framework hosts
are no longer supported.

## Commands

Everything lives under `src/` (the solution is `src/RestrictedProcess.sln`):

```powershell
dotnet build src/RestrictedProcess.sln          # build; also produces the NuGet package (GeneratePackageOnBuild)
dotnet test --solution src/RestrictedProcess.sln    # run all tests (~15 s, serialized)
dotnet test --project src/RestrictedProcess.Tests --filter-method "*ShouldNotBeAbleToCreateFiles"   # single test
dotnet test --project src/RestrictedProcess.Tests --filter-class "*SandboxIsolationTests*"
```

`dotnet test` runs in the Microsoft.Testing.Platform (MTP) mode of the .NET 10 SDK — opted in via
`test.runner` in the repo-root `global.json`. The test project targets `net10.0-windows` and uses xunit.v3
(no `Microsoft.NET.Test.Sdk` or `xunit.runner.visualstudio`; the test assembly is a self-contained runner
exe that can also be executed directly with e.g. `-method "*Name"`).

Tests compile small C# programs to `.exe` at runtime and actually execute them in the sandbox, so they need
a real Windows session. **They run serially** (`src/RestrictedProcess.Tests/xunit.runner.json`) — they
measure processor time and memory, and parallel execution makes those numbers a function of machine load.
They now run in CI as well; the historical "Access is denied" that kept them out was
`CREATE_BREAKAWAY_FROM_JOB` (see below), not an inherent CI limitation. Clipboard tests use `[StaFact]`
(Xunit.StaFact), including the clipboard *write* test, whose assertion reads the clipboard from the test
host thread.

## Architecture

Two layers: public executors on top, raw Win32 process machinery below.

- **`IExecutor`** — `ExecuteAsync(ExecutionRequest, CancellationToken)` plus a blocking `Execute`.
  `ExecutionRequest` carries the file, arguments, stdin, and the limits; `ProcessExecutionResult` carries
  output, exit code, wall/processor time, both memory metrics, I/O counters and a
  `ProcessExecutionResultType` (Success / TimeLimit / MemoryLimit / RunTimeError / OutputLimit / Cancelled).
- **`RestrictedProcessOptions`** — the sandbox profile, reusable across runs. Token level, integrity level,
  mitigations (both policy words), desktop and window station, AppContainer/LPAC, job priority/affinity/
  CPU-rate, output caps, memory metric, writable directories, environment scrubbing, limit multipliers.
- **`RestrictedProcessExecutor`** — orchestrates one run: async stdin write, bounded stdout/stderr reads,
  a single `WaitAnyAsync` over {process exited, job notification limit} with the wall-clock deadline and
  the cancellation token, then classification. No polling anywhere.
- **`StandardProcessExecutor`** — non-sandboxed fallback on `System.Diagnostics.Process`.
- **`Process/RestrictedProcess.cs`** — the core. Creates the pipes, builds the token, optionally the
  AppContainer profile and the desktop, **creates the job object first**, then starts the process suspended
  via `CreateProcessAsUser` with a STARTUPINFOEX attribute list. `Start()` only resumes the thread.
- **`Process/RestrictedTokenBuilder.cs`** — the token. Enumerates `TokenGroups` and makes every group
  deny-only except the level's exception list, builds the restricting-SID set, drops privileges, narrows
  the default DACL, applies the integrity level, hardens the token's own label.
- **`Process/SidFactory.cs`** — well-known SIDs and the unique per-run SID (`S-1-5-21-` + 128 random bits).
- **`Process/SandboxDesktop.cs`** — a throwaway `rp_<guid>` desktop with an explicitly built DACL and a
  mandatory label derived from the configured integrity level.
- **`Process/AppContainerProfile.cs`** — a reused, stably named AppContainer profile plus a reverted ACL
  grant on the executable.
- **`Process/CommandLine.cs`** — `CommandLineToArgvW`-compatible quoting.
- **`Process/ProcThreadAttributeList.cs`** — the STARTUPINFOEX attributes. Values live in HGlobal blocks
  that must stay alive until `CreateProcessAsUser` returns.
- **`JobObjects/`** — `JobObject` (SafeHandle based), `PrepareJobObject` (hard limits + soft notification
  limits + UI restrictions), and `JobNotificationListener` (the completion-port pump).

### Limit enforcement is two-tiered (important when changing limits)

The job object gets `JobLimitsMultiplier`× (default **2×**) the requested memory as a hard OS backstop. The
*exact* limits are applied as job **notification** limits (`JobObjectNotificationLimitInformation`), which
report a breach to an I/O completion port without failing an allocation or terminating anything. That is
why the memory limit uses the job-wide `JOB_OBJECT_LIMIT_JOB_MEMORY` and not the per-process one: the
program must be allowed to allocate past the limit for the overage to be measurable, and a per-process
commit cap fails the allocation atomically (committed memory never grows), which would misreport an
over-limit program as a runtime error. The hard `JOB_OBJECT_LIMIT_JOB_TIME` stays off for the same reason —
it terminates the job and loses the measurement; the same threshold is a notification limit instead.

### Hard-won details — do not undo these

- **No `CREATE_BREAKAWAY_FROM_JOB`.** It fails with `ERROR_ACCESS_DENIED` whenever the caller is already
  inside a job that lacks `JOB_OBJECT_LIMIT_BREAKAWAY_OK` — which is the case under `dotnet test` and under
  CI runners. On master before the rework, **all 29 tests failed** for this reason. Nested jobs have been
  supported since Windows 8; the job is attached at creation with `PROC_THREAD_ATTRIBUTE_JOB_LIST` instead.
- **A desktop's mandatory label must be written with `SetSecurityInfo(SE_WINDOW_OBJECT,
  LABEL_SECURITY_INFORMATION, …)`.** `SetUserObjectSecurity` returns success and silently does not apply
  it, leaving an unlabelled (implicitly Medium) desktop that a Low integrity process cannot attach to; the
  child then dies during user32 initialisation with `ERROR_DLL_INIT_FAILED` and no useful diagnostic.
  Supplying the label as a SACL to `CreateDesktop` does not work either — any SACL there needs
  `SeSecurityPrivilege` and fails with `ERROR_PRIVILEGE_NOT_HELD`.
- **The desktop DACL must grant the logon SID**, taken from the token that was actually built (see
  `SandboxToken`), not re-derived from `WindowsIdentity` — that lookup can come back empty. The logon SID
  is the one group that stays enabled at every token level, so it is what the first access check matches;
  the unique run SID is what the restricting-SID check matches. The desktop owner also needs full control,
  or the label cannot be applied afterwards.
- **`BlockNetworkAccess` and `UseAlternateDesktop` are mutually exclusive** and the combination is rejected
  in `ValidateOptions`. An AppContainer process cannot attach to a desktop this library creates. This was
  tested against every desktop security descriptor available — any DACL (including `GENERIC_ALL` to
  Everyone), any mandatory label (Low, Untrusted, none), on the current window station and on a private
  one — and the child always died with `ERROR_DLL_INIT_FAILED`. Only the inherited desktop works.
- **The per-run SID protects objects that take the token default DACL, and nothing else.** A named object
  created under `Local\` lands in the session BaseNamedObjects directory and inherits that directory's
  ACEs instead, so a second run *can* open it - this was measured, so do not claim otherwise in the docs.
- **The token default DACL must not grant the logon SID.** Granting it would let any process in the same
  logon session — including another sandboxed run, whose restricting SIDs also contain the logon SID —
  open the objects a run creates. The process can still use its own BaseNamedObjects directory because the
  logon SID remains a *group* on the token.
- **`StartupInfo` pipe handles must be disposed right after `CreateProcessAsUser`**, otherwise reading
  stdout/stderr hangs forever. The attribute-list *values* have the same lifetime requirement.
- **The `RestrictedProcess` stdio streams are intentionally not disposed** in `Dispose()` — doing so throws
  `InvalidOperationException` while an async read is in flight.
- The stdio pipes default to the system's **ANSI code page** (via `GetACP`), not `Encoding.Default`, which
  is UTF-8 on modern .NET while console children write redirected output in the ANSI code page.
- Some `ProcessMitigations` break the CLR and are off by default: `Win32kSystemCallDisable` (user32 cannot
  initialize) and `ProhibitDynamicCode` (blocks the JIT). Safe only for native executables.
- `CpuRateLimitPercent` is a percentage of **total** system CPU across all cores, so a single-threaded
  program is only throttled below `100 / ProcessorCount`.
- **The job's memory and processor time notification limits fire reliably; the disk write one does not**
  (at least on ordinary NTFS volumes). `MaxDiskWriteBytes` is therefore enforced twice: as a notification,
  and by comparing the job's accumulated write counter after the run. A probe confirmed the memory
  notification working - a program allocating 200 MB against a 32 MB limit is killed after ~450 ms with a
  peak commit of 33 MB, rather than running to completion.
- `TokenLevel.StrictlyRestricted` and `TokenLevel.Lockdown` **cannot start a managed executable** — the
  process exits with `STATUS_ACCESS_DENIED` before its entry point. This is asserted by a test so the
  documentation cannot drift.

## Tests

`BaseExecutorsTestClass.CreateExe(name, sourceCode)` compiles a C# source string into an exe under `Exe\`
in the test output directory via Roslyn, referencing `mscorlib`/`System`/`System.Windows.Forms` from the
.NET Framework directory built into Windows — so the produced exes are standalone .NET Framework
executables the sandbox can run directly, and they can P/Invoke freely to observe the token, mitigation
policies, desktop and so on from inside the sandbox.

`Request(...)` builds a request whose wall-clock deadline is derived from the processor time limit;
`UntimedRequest(...)` gives a generous 30 s wall clock and is what every test that is *not* measuring time
should use, so a loaded machine cannot turn an assertion into a spurious time limit.

- `RestrictedProcessTests` — I/O, encoding, time, memory, output flooding, exit codes, priority, affinity,
  CPU rate.
- `RestrictedProcessSecurityTests` — the sandbox blocks file creation, clipboard access, process spawning,
  handle inheritance and parent-environment access, and the token / integrity level / mitigations /
  alternate desktop are really applied.
- `SandboxIsolationTests` — what is and is not contained: reads at the default level, the read-containing
  levels being unusable for managed code, `WriteRestricted` writing only where granted (and the grant being
  removed afterwards), the unique per-run SID, exit code 259, spaces and quotes in paths and arguments,
  cancellation, the rejected option combination, and the capability probe.

## Code style

StyleCop.Analyzers is enforced at build via `src/Rules.ruleset` and `src/stylecop.json`. Notable required
conventions: `using` directives inside the namespace (System first, blank line between groups), newline at
end of file, files stored as **UTF-8 with BOM** (SA1412) and CRLF, and a file header comment with copyright
for company "Nikolay Kostov (Nikolay.IT)" — see any existing `.cs` file for the exact header.
