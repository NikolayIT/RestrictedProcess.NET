# RestrictedProcess.NET

[![CI](https://github.com/NikolayIT/RestrictedProcess.NET/actions/workflows/ci.yml/badge.svg)](https://github.com/NikolayIT/RestrictedProcess.NET/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/RestrictedProcess.NET.svg)](https://www.nuget.org/packages/RestrictedProcess.NET/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A small .NET library for running untrusted Windows executables in a sandbox with restricted rights, while enforcing time and memory limits. Ideal for scenarios like online judges, code grading systems, or any situation where you need to execute code you don't trust.

> **Windows only.** The library targets .NET Standard 2.0, .NET 8 and .NET 10, but it is built on Win32 APIs (restricted tokens, job objects, `CreateProcessAsUser`), so it works exclusively on Windows.

## Installation

```
dotnet add package RestrictedProcess.NET
```

## Usage

```csharp
using RestrictedProcess;

IExecutor executor = new RestrictedProcessExecutor();

ProcessExecutionResult result = executor.Execute(
    @"C:\path\to\untrusted.exe",
    inputData: "input passed to standard input",
    timeLimit: 1000,                 // milliseconds
    memoryLimit: 32 * 1024 * 1024);  // bytes

Console.WriteLine(result.Type);           // Success, TimeLimit, MemoryLimit, RunTimeError or OutputLimit
Console.WriteLine(result.ReceivedOutput); // what the process wrote to standard output
Console.WriteLine(result.ErrorOutput);    // what the process wrote to standard error
Console.WriteLine(result.ExitCode);
Console.WriteLine(result.TimeWorked);
Console.WriteLine(result.MemoryUsed);     // peak working set in bytes
```

If you don't need sandboxing, `StandardProcessExecutor` implements the same `IExecutor` interface using a regular `System.Diagnostics.Process` (time limit only, no memory constraints).

Both executors optionally accept a `Microsoft.Extensions.Logging.ILogger` in their constructor for diagnostic output; without one, logging is a no-op.

## What the sandbox restricts

The process is started with a restricted token at low integrity level and placed in a Win32 job object, which means it:

- Cannot create or write files (low integrity level)
- Cannot read from or write to the clipboard
- Cannot start other processes (kernel-level child-process ban plus an active process limit of 1)
- Cannot change display settings, exit Windows, or access global atoms and system parameters
- Cannot use administrative rights even in an elevated host (all privileges are dropped, Administrators becomes deny-only, and restricting SIDs are applied)
- Cannot see the parent's handles (only its three standard IO pipes are inherited) or the parent's environment variables (it gets a minimal environment block)
- Runs on a throwaway desktop, so it cannot read or message windows on the interactive desktop
- Runs with process mitigation policies enabled (DEP, ASLR, strict handle checks, extension-point disable, image-load restrictions)
- Can optionally be denied all network access (opt-in, see below)
- Is killed automatically when the job handle is closed or on an unhandled exception
- Is limited in the amount of memory it can commit

Time and memory limits are enforced precisely by the executor: the process is killed if it exceeds the wall-clock allowance, its total processor time is compared against the time limit, and its peak memory is compared against the memory limit. Output is read up to a configurable cap, so a program that floods its output cannot exhaust the host's memory (the run is then reported as `OutputLimit`).

## Configuring the sandbox

Pass a `RestrictedProcessOptions` to the executor to adjust the hardening. Every option defaults to the strongest setting a plain console executable tolerates:

```csharp
var options = new RestrictedProcessOptions
{
    TokenLevel = TokenLevel.Restricted,             // Unrestricted | Limited | Restricted
    IntegrityLevel = IntegrityLevel.Low,            // Untrusted | Low | Medium
    DisallowChildProcesses = true,
    RestrictInheritedHandles = true,
    ScrubEnvironment = true,
    UseAlternateDesktop = true,
    Mitigations = ProcessMitigations.Default,
    ActiveProcessLimit = 1,
    PriorityClass = ProcessPriorityClass.High,
    CpuRateLimitPercent = null,                     // e.g. 25 caps the job to 25% of total CPU
    MaxOutputSize = 64 * 1024 * 1024,
    BlockNetworkAccess = false,                     // opt-in; see below
};

IExecutor executor = new RestrictedProcessExecutor(options);
```

A few mitigations are available but **off by default because they break the .NET Framework runtime**: `ProcessMitigations.Win32kSystemCallDisable` (user32 cannot initialize) and `ProcessMitigations.ProhibitDynamicCode` (blocks the JIT). Enable them only for native executables that don't need those subsystems.

### Blocking network access

Set `BlockNetworkAccess = true` to deny the process all network access. It is then launched inside an [AppContainer](https://learn.microsoft.com/windows/win32/secauthz/appcontainer-isolation) with no network capabilities, so the Windows Firewall blocks its sockets (including localhost). It is off by default because it has a few requirements:

- The **Windows Firewall / Base Filtering Engine service must be running** — that service is what enforces the block.
- The library grants the "ALL APPLICATION PACKAGES" identity read and execute rights on the executable (via `icacls`) so the AppContainer can load it, and creates a temporary AppContainer profile that it deletes when the process is disposed.
- All the other hardening still applies: the throwaway desktop (its ACL grants the AppContainer identity) and the scrubbed environment are both kept — the environment just gains the handful of profile path variables the AppContainer needs to start (paths, not secrets).

## Building and testing

The solution lives under `src/`:

```
dotnet build src/RestrictedProcess.sln
dotnet test src/RestrictedProcess.sln
```

The tests compile small C# programs at runtime (via Roslyn, targeting the .NET Framework built into Windows) and actually execute them in the sandbox, so they must be run locally on Windows. They are not run on CI: creating a process with a restricted token fails with "Access is denied" in hosted build environments.

## History

From 2013 till 2015 the `RestrictedProcess` library was part of another project of mine called [Open Judge System](https://github.com/NikolayIT/OpenJudgeSystem).

Since the code was useful there, I thought it may be useful in other projects as well, so I moved it into a separate repository and gave it the right to be independent ;)

## License

This project is licensed under the [MIT License](LICENSE).
