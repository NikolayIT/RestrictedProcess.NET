# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A Windows-only .NET library for running untrusted executables in a sandbox with time and memory limits (originally extracted from the Open Judge System). The library multi-targets `netstandard2.0`, `net8.0` and `net10.0`, but works exclusively on Windows — the core is built on Win32 P/Invoke (`CreateProcessAsUser`, restricted tokens, job objects). The modern targets carry an assembly-level `[SupportedOSPlatform("windows")]` attribute (generated from the csproj).

## Commands

Everything lives under `src/` (the solution is `src/RestrictedProcess.sln`):

```powershell
dotnet build src/RestrictedProcess.sln          # build; also produces the NuGet package (GeneratePackageOnBuild)
dotnet test --solution src/RestrictedProcess.sln    # run all tests
dotnet test --project src/RestrictedProcess.Tests --filter-method "*ShouldNotBeAbleToCreateFiles"   # single test
```

`dotnet test` runs in the Microsoft.Testing.Platform (MTP) mode of the .NET 10 SDK — opted in via `test.runner` in the repo-root `global.json`. The test project targets `net10.0-windows` and uses xunit.v3 (no `Microsoft.NET.Test.Sdk` or `xunit.runner.visualstudio` — the test assembly is a self-contained runner exe that can also be executed directly with e.g. `-method "*Name"`).

Tests require Windows and must run locally: they compile small C# programs to `.exe` files at runtime and actually execute them in the sandbox, and `CreateProcessAsUser` with a restricted token fails with "Access is denied" in hosted CI environments (the GitHub workflow builds only, deliberately). Clipboard tests use `[StaFact]` (Xunit.StaFact) instead of `[Fact]` — including the clipboard *write* test, whose assertion reads the clipboard from the test host thread.

## Architecture

Two-layer design: public executors on top, raw Win32 process machinery below.

- **`IExecutor`** — the public entry point: `Execute(fileName, inputData, timeLimit, memoryLimit, args)` returns a `ProcessExecutionResult` (output, error, exit code, time/memory used, and a `ProcessExecutionResultType`: Success / TimeLimit / MemoryLimit / RunTimeError).
- **`RestrictedProcessExecutor`** — the sandboxed implementation. Orchestrates async stdin write / stdout+stderr reads, samples `PeakWorkingSetSize` every 45 ms on a background task and combines it with the job object's `PeakJobMemoryUsed` (reliable even for short-lived processes) to track peak memory, and classifies the result. Both executors take an optional `Microsoft.Extensions.Logging.ILogger` constructor parameter (no-op `NullLogger` by default).
- **`StandardProcessExecutor`** — non-sandboxed fallback using plain `System.Diagnostics.Process` (memory constraints not implemented).
- **`Process/RestrictedProcess.cs`** — the core. A manual reimplementation of `Process.Start`: creates a restricted token (`CreateRestrictedToken` + low integrity mandatory label), starts the process **suspended** via `CreateProcessAsUser`, and redirects stdio through manually created inheritable pipes. `Start()` then assigns the process to a job object and resumes the main thread.
- **`JobObjects/`** — wrapper over Win32 job objects. `PrepareJobObject` configures the limits: job memory limit, single active process, die-on-unhandled-exception, kill-on-job-close, plus UI restrictions (clipboard, desktop, display settings, global atoms, etc.). The job-time limit flags are deliberately disabled (they caused unexpected behavior).
- P/Invoke signatures are split into `Process/NativeMethods.cs` and `JobObjects/NativeMethods.cs`; the rest of the files in those folders are marshaling structs/enums mirroring Win32 types.

### Limit enforcement is two-tiered (important when changing limits)

The job object gets **2×** the requested time/memory limits as a hard OS-level backstop (`Start(timeLimit, memoryLimit)` doubles them). The *precise* limits are enforced in `RestrictedProcessExecutor`: the process is killed after 1.5× `timeLimit` wall-clock, then `TotalProcessorTime` is compared against `timeLimit` and sampled peak memory against `memoryLimit` to set the result type. Any non-empty stderr output classifies the run as `RunTimeError`.

### Known-fragile spots (documented in code comments)

- The `StartupInfo` pipe handles must be disposed right after `CreateProcessAsUser`, otherwise reading stdout/stderr hangs forever.
- The `RestrictedProcess` stdio streams are intentionally *not* disposed in `Dispose()` (doing so throws `InvalidOperationException` due to in-flight async operations).
- The stdio pipes default to the system's **ANSI code page** (via `GetACP`), not `Encoding.Default` — on modern .NET `Encoding.Default` is UTF-8, but console child processes write redirected output in the ANSI code page. An explicit `Encoding` can be passed to the `RestrictedProcess` constructor.
- On modern Windows, shell-brokered executables (e.g. `notepad.exe`, which is a packaged-app alias) are launched by a system service *outside* the job object, so the active-process limit cannot block them; only direct `CreateProcess` calls (e.g. `cmd.exe` with `UseShellExecute = false`) are blocked.

## Tests

`BaseExecutorsTestClass.CreateExe(name, sourceCode)` compiles a C# source string into an exe under `Exe\` in the test output directory via Roslyn (`Microsoft.CodeAnalysis.CSharp`), referencing `mscorlib`/`System`/`System.Windows.Forms` from the .NET Framework directory built into Windows — so the produced exes are standalone .NET Framework executables the sandbox can run directly. Security tests (`RestrictedProcessSecurityTests`) assert the sandbox blocks file creation, clipboard read/write, process spawning, etc., by expecting `RunTimeError`; behavior tests (`RestrictedProcessTests`) cover I/O, time, and memory limits.

## Code style

StyleCop.Analyzers is enforced at build via `src/Rules.ruleset` and `src/stylecop.json`. Notable required conventions: `using` directives inside the namespace (System first, blank line between groups), newline at end of file, and a file header comment with copyright for company "Nikolay Kostov (Nikolay.IT)" (see any existing `.cs` file for the exact header).
