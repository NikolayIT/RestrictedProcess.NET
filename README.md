# RestrictedProcess.NET

[![CI](https://github.com/NikolayIT/RestrictedProcess.NET/actions/workflows/ci.yml/badge.svg)](https://github.com/NikolayIT/RestrictedProcess.NET/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/RestrictedProcess.NET.svg)](https://www.nuget.org/packages/RestrictedProcess.NET/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A .NET library for running untrusted Windows executables in a sandbox with restricted rights, while
enforcing time and memory limits. Built for online judges, code grading systems, and anywhere else you
have to execute code you do not trust.

> **Windows only.** The library targets .NET 8 and .NET 10 on Windows. It is built directly on Win32
> (restricted tokens, job objects, `CreateProcessAsUser`), so there is no cross-platform story.

## Installation

```
dotnet add package RestrictedProcess.NET
```

## Usage

```csharp
using RestrictedProcess;

IExecutor executor = new RestrictedProcessExecutor();

var result = await executor.ExecuteAsync(new ExecutionRequest(@"C:\path\to\untrusted.exe")
{
    Input          = "input passed to standard input",
    CpuTimeLimit   = TimeSpan.FromSeconds(1),      // what the verdict is based on
    WallClockLimit = TimeSpan.FromSeconds(3),      // when the process is killed regardless
    MemoryLimitBytes = 256L * 1024 * 1024,
});

Console.WriteLine(result.Type);               // Success, TimeLimit, MemoryLimit, RunTimeError, OutputLimit, Cancelled
Console.WriteLine(result.ReceivedOutput);
Console.WriteLine(result.ErrorOutput);
Console.WriteLine(result.ExitCode);
Console.WriteLine(result.TotalProcessorTime); // across the whole job, including exited children
Console.WriteLine(result.TimeWorked);         // wall clock
Console.WriteLine(result.MemoryUsed);         // peak committed bytes by default
```

`ExecuteAsync` takes a `CancellationToken`; cancelling kills the process and returns a `Cancelled`
result. There is also a blocking `Execute(ExecutionRequest)` for synchronous callers — it blocks the
calling thread, so if you are judging many submissions concurrently, prefer `ExecuteAsync`.

Set only `CpuTimeLimit` and the wall-clock deadline is derived from it
(`WallClockWaitMultiplier`, 1.5× by default). The two are separate because a program that sleeps burns no
processor time: a single limit either lets it idle indefinitely or punishes programs that block on
legitimate I/O.

If you do not need sandboxing, `StandardProcessExecutor` implements the same interface with a plain
`System.Diagnostics.Process`. It applies the time limit and captures output, and nothing else — no memory
limit, no privilege reduction, no isolation.

Both executors accept an optional `Microsoft.Extensions.Logging.ILogger`.

## What the sandbox restricts

The process runs under a restricted token at a low integrity level, inside a job object it is attached to
at creation time, on a throwaway desktop. Concretely, it:

- Cannot create or write files anywhere the low integrity level does not allow (which is almost nowhere)
- Cannot read from or write to the clipboard
- Cannot start other processes — a kernel-level child-process ban, an active-process limit of 1, and a
  desktop-app breakaway block so a shell-brokered launch cannot escape the job
- Cannot change display settings, exit Windows, or reach global atoms and system parameters
- Cannot use administrative rights even when the host is elevated: every privilege is dropped and every
  group in the token is turned into a deny-only group
- Cannot see the parent's handles (only its three standard I/O pipes are inherited) or the parent's
  environment (it gets a minimal block)
- Cannot enumerate, read, or send window messages to windows on the interactive desktop
- Cannot hook, journal-record, or take ownership of even its own desktop
- Gets a freshly generated SID per execution, which is both a restricting SID on its token and the only
  non-user entry in its default DACL, so the objects a run creates are not reachable by another run
  (see the caveat below)
- Runs with process creation mitigation policies applied (DEP, ASLR, strict handle checks, extension-point
  disable, image-load restrictions)
- Can optionally be denied all network access (opt-in, see below)
- Is killed when the job handle closes or on an unhandled exception

### What it does not restrict

This section matters as much as the one above.

- **Reads.** At the default token level the restricting SIDs include `Everyone` and `BUILTIN\Users`, and a
  low integrity level only blocks *writes*, so a sandboxed program can read anything whose ACL grants those
  groups. Concretely, that is the system directories and **anything created off a drive root**, which
  inherits `BUILTIN\Users:(RX)` from it - `C:\Data`, `C:\inetpub`, a judge's own working folder.
  It is **not** the user profile: `C:\Users\<name>` blocks inheritance and grants only SYSTEM,
  Administrators and the user, none of which are restricting SIDs, so Documents, Desktop, AppData and
  `%TEMP%` are refused. There is a test covering both halves of that.
  If you keep test data or expected output somewhere the sandbox can reach, take `Users` and `Everyone` off
  its ACL - that is the fix, not a stricter token level. The levels that contain all reads
  (`StrictlyRestricted`, `Lockdown`) also deny the runtime the program needs to load, so a managed
  executable never reaches its entry point under them; they are for statically linked native binaries.
  `WriteRestricted` is the practical middle ground when you need a writable scratch directory.
- **Named objects in the session namespace.** The per-run SID protects objects that take the token's
  *default* DACL. An object created under `Local\` lands in the session's `BaseNamedObjects` directory and
  picks up that directory's inheritable ACEs instead, so one sandboxed run can still open a named event or
  mutex created by another. If two runs must not be able to signal each other, do not rely on this.
- **Non-securable resources.** Anything with a null or absent security descriptor — FAT/exFAT volumes,
  some anonymous shared memory — is outside what a token can protect.
- **Threads.** A job object limits processes, not threads. A program that spawns thousands of threads is
  bounded only by the CPU rate cap and the memory limit.
- **Disk writes**, unless you set `MaxDiskWriteBytes` - which is reported as an `OutputLimit`, and
  is checked both from a job notification (which stops the program mid-write where the OS delivers
  it) and from the job's accumulated write counter after the run (which is what actually catches it
  on an ordinary NTFS volume).
- **The network**, unless you set `BlockNetworkAccess` — and that depends on the Windows Firewall service
  actually running.

`SandboxCapabilities.Probe()` reports what the current machine supports (private desktops, job
notification limits, extended mitigations, whether the firewall is running) so a host can check up front
instead of discovering it through a failed run.

## Configuring the sandbox

Pass a `RestrictedProcessOptions` to the executor. Every option defaults to the strongest setting a plain
console executable tolerates.

```csharp
var options = new RestrictedProcessOptions
{
    TokenLevel = TokenLevel.Restricted,   // Unrestricted | Limited | Restricted | WriteRestricted
                                          // | StrictlyRestricted | Lockdown
    IntegrityLevel = IntegrityLevel.Low,  // Untrusted | Low | Medium
    Mitigations = ProcessMitigations.Default,
    Mitigations2 = ProcessMitigations2.None,   // CET, FSCTL disable, module tampering, core sharing
    DisallowChildProcesses = true,
    RestrictInheritedHandles = true,
    LockdownTokenDefaultDacl = true,
    HardenTokenIntegrityPolicy = true,
    ScrubEnvironment = true,
    UseAlternateDesktop = true,
    UseAlternateWindowStation = false,
    ActiveProcessLimit = 1,
    PriorityClass = ProcessPriorityClass.High,
    CpuRateLimitPercent = null,           // e.g. 25 caps the job to 25% of total CPU
    MemoryMetric = MemoryMetric.PeakCommit,
    MaxOutputSize = 64 * 1024 * 1024,
    MaxDiskWriteBytes = null,
    BlockNetworkAccess = false,
};
```

### Token levels

| Level | Deny-only groups | Restricting SIDs | Starts a managed exe? |
|---|---|---|---|
| `Unrestricted` | none | none | yes |
| `Limited` | all but `Users`, `Everyone`, `INTERACTIVE` | none | yes |
| `Restricted` *(default)* | all but `Users`, `Everyone`, `INTERACTIVE` | `Users`, `Everyone`, `RESTRICTED`, logon, unique | yes |
| `WriteRestricted` | administrative groups (LUA token) | write access only: unique, logon, `Everyone` | yes |
| `StrictlyRestricted` | all | `RESTRICTED`, logon, unique | no |
| `Lockdown` | all, including the user | `S-1-0-0`, unique | no |

`WriteRestricted` is the level to reach for when the program needs somewhere to write: reads keep working
so the binary starts, and writes only succeed where the run's unique SID has been granted access. Pair it
with `WritableDirectories`:

```csharp
var options = new RestrictedProcessOptions { TokenLevel = TokenLevel.WriteRestricted };
options.WritableDirectories.Add(scratchDirectory);
```

The grant is made before the process starts and removed when it ends, and it names a SID generated for
that single execution — so two concurrent runs pointed at the same directory still cannot write into each
other's files.

### Blocking network access

`BlockNetworkAccess = true` runs the program inside an AppContainer with no capabilities, which makes the
Windows Firewall drop its sockets. Two things to know:

- It needs the Windows Firewall / Base Filtering Engine service running. With the service stopped there is
  no network boundary at all. `SandboxCapabilities.Probe().FirewallRunning` tells you.
- It **cannot be combined with `UseAlternateDesktop`**. A process in an AppContainer cannot attach to a
  desktop this library creates — it dies during user32 initialisation regardless of how the desktop is
  secured. The combination is rejected at construction rather than producing a process that will not
  start, so set `UseAlternateDesktop = false` when you turn network blocking on. The job object still
  denies clipboard access, global atoms, and USER handles from outside the job.

## How limits are enforced

Two tiers, and the split is deliberate:

- The **job object** gets a loosened hard backstop — `JobLimitsMultiplier` (2×) the requested memory — so
  the OS stops a runaway program without the host ever being at risk. The job-wide committed memory limit
  is used rather than the per-process one on purpose: a per-process commit cap fails the allocation
  atomically, so committed memory never grows and an over-limit program looks like a runtime error instead
  of a memory limit.
- The **exact limits** are applied as job *notification* limits. Crossing one does not fail an allocation
  or terminate anything; it posts a message to an I/O completion port, so the breach is observed the
  instant it happens and the run is stopped without waiting out the clock — while the overage stays
  measurable.

Processor time is read from the job's accounting information, so it covers every process in the tree
including any that exited early. Memory defaults to peak committed bytes (`MemoryMetric.PeakCommit`),
which is reproducible across machines; `PeakWorkingSet` is also exposed but depends on system memory
pressure, and `Max` reproduces the historical behaviour of taking the larger of the two.

## Diagnostics

Every Win32 failure during sandbox setup throws a `SandboxException` naming the step that failed
(`SandboxStep.CreateProcess`, `SandboxStep.CreateDesktop`, `SandboxStep.CreateRestrictedToken`, …) along
with the underlying error code, so an "Access is denied" points at the call that produced it.

## Requirements

- Windows 10 1703 or later is assumed. Extended mitigation policies need that build; everything else works
  on earlier versions, and `SandboxCapabilities.Probe()` reports what is available.
- No elevation is required. Running the host elevated does not weaken the sandbox — administrative groups
  are deny-only in the child's token either way.
