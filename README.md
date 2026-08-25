# RestrictedProcess.NET

[![CI](https://github.com/NikolayIT/RestrictedProcess.NET/actions/workflows/ci.yml/badge.svg)](https://github.com/NikolayIT/RestrictedProcess.NET/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/RestrictedProcess.NET.svg)](https://www.nuget.org/packages/RestrictedProcess.NET/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A small .NET library for running untrusted Windows executables in a sandbox with restricted rights, while enforcing time and memory limits. Ideal for scenarios like online judges, code grading systems, or any situation where you need to execute code you don't trust.

> **Windows only.** The library targets .NET Standard 2.0, but it is built on Win32 APIs (restricted tokens, job objects, `CreateProcessAsUser`), so it works exclusively on Windows.

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

Console.WriteLine(result.Type);           // Success, TimeLimit, MemoryLimit or RunTimeError
Console.WriteLine(result.ReceivedOutput); // what the process wrote to standard output
Console.WriteLine(result.ErrorOutput);    // what the process wrote to standard error
Console.WriteLine(result.ExitCode);
Console.WriteLine(result.TimeWorked);
Console.WriteLine(result.MemoryUsed);     // peak working set in bytes
```

If you don't need sandboxing, `StandardProcessExecutor` implements the same `IExecutor` interface using a regular `System.Diagnostics.Process` (time limit only, no memory constraints).

## What the sandbox restricts

The process is started with a restricted token at low integrity level and placed in a Win32 job object, which means it:

- Cannot create or write files (low integrity level)
- Cannot read from or write to the clipboard
- Cannot start other processes (active process limit of 1)
- Cannot change display settings, exit Windows, or access global atoms and system parameters
- Is killed automatically when the job handle is closed or on an unhandled exception
- Is limited in the amount of memory it can commit

Time and memory limits are enforced precisely by the executor: the process is killed if it exceeds the wall-clock allowance, its total processor time is compared against the time limit, and its peak working set is sampled continuously and compared against the memory limit.

## Building and testing

The solution lives under `src/`:

```
dotnet build src/RestrictedProcess.sln
dotnet test src/RestrictedProcess.sln
```

The tests compile small C# programs at runtime and actually execute them in the sandbox, so they require Windows and .NET Framework 4.6.1.

## History

From 2013 till 2015 the `RestrictedProcess` library was part of another project of mine called [Open Judge System](https://github.com/NikolayIT/OpenJudgeSystem).

Since the code was useful there, I thought it may be useful in other projects as well, so I moved it into a separate repository and gave it the right to be independent ;)

## License

This project is licensed under the [MIT License](LICENSE).
