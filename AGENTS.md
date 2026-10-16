# AGENTS

Guidance for AI agents working in the Spice86 repository.

## Project Overview
Spice86 is a .NET 10 cross-platform emulator for reverse engineering real-mode DOS programs. It enables running, analyzing, and incrementally rewriting DOS binaries in C# without source code.

## Architecture & Module Boundaries

### Project Structure
- **`Spice86.Core`**: Core emulation engine (CPU, memory, devices, DOS/BIOS handlers)
- **`Spice86`**: Main application with Avalonia UI (ViewModels, Views, manual composition root)
- **`Bufdio.Spice86`**: Audio subsystem (PortAudio bindings)
- **`Spice86.Logging`**: Serilog-based logging infrastructure
- **`Spice86.Shared`**: Shared interfaces and utilities
- **`Spice86.Tests`**: XUnit tests with FluentAssertions and NSubstitute

### Dependency Injection
The entire emulator is assembled in `Spice86DependencyInjection.cs` (~600 lines):
- Constructor creates the full object graph with explicit dependencies (no IoC container)
- Order matters: components are constructed in dependency order
- Components are wired together with event handlers and shared state
- Entry point is `Program.cs` which instantiates `Spice86DependencyInjection`
- **`Spice86DependencyInjection` is the central composition root** - understand its structure when working with dependencies
- The `Machine` class aggregates emulator components (CPU, memory, devices) - access via properties like `CfgCpu`, `Memory`, `Stack`
- `InterruptVectorTable` and `Stack` are now passed directly to `Machine` constructor

### CPU Execution Model
**`CfgCpu`** is the sole CPU implementation (Control Flow Graph-based executor):
- Builds dynamic CFG for analysis and future JIT compilation
- Tracks instruction variants for self-modifying code via selector nodes
- Maintains execution context hierarchy for hardware interrupts
- See `doc/cfgcpuReadme.md` for detailed CFG architecture

## Critical Workflows

### Building & Running
```text
# Build from repository root
dotnet build src/Spice86.sln

# Run with executable
dotnet run --project src/Spice86 -- -e path\to\program.exe

# Run tests (excluding SingleStepTest - see note below)
dotnet test tests/Spice86.Tests -- --filter-not-class '*SingleStepTest*'

# Run a single test class / method
dotnet test tests/Spice86.Tests -- --filter-class '*GeneratedCodeMachineTest*'
dotnet test tests/Spice86.Tests -- --filter-method '*TestStiPending*'

# Opt-in: compile the C# every test generates and append one row per test to a TSV coverage report (path must be absolute)
SPICE86_GENERATED_CODE_COVERAGE=$PWD/tmp/generated-code-coverage.tsv dotnet test tests/Spice86.Tests -- --filter-not-class '*SingleStepTest*' --filter-not-class '*GeneratedCodeCoverageReportTest*'
```

> **Test filter syntax rule**: the test project runs on xUnit v3 through Microsoft.Testing.Platform (`TestingPlatformDotnetTestSupport` in `tests/Directory.Build.props`). The VSTest-style `--filter 'FullyQualifiedName~...'` is **silently ignored** (warning `MTP0001`) and runs the whole suite, SingleStepTest included. Always pass filters after `--` using `--filter-class`, `--filter-not-class`, `--filter-method`, `--filter-not-method` with `*` wildcards.

> **SingleStepTest exclusion rule**: `SingleStepTest` runs millions of CPU instruction test cases and take an extremely long time to complete. **Always exclude it** using `-- --filter-not-class '*SingleStepTest*'` unless the change being tested directly touches CPU instruction decoding, execution, or flag handling (e.g. changes to `CfgCpu`, instruction parsers, ALU operations, or flag computation). When in doubt, exclude it.

> **Test output rule (agents only)**: no need to redirect `dotnet test` output to a file. On success the console summary is short (a single `Passed! - Failed: 0, Passed: N ...` line), so run it directly and read the summary. Only capture to a file when a run actually fails and you need the full failure detail.

> **Direct failure diagnostics**: if the MTP summary points to an inaccessible ignored `bin/` log, `dotnet run --project tests/Spice86.Tests --no-build -- -class- '*SingleStepTest*' -reporter quiet -noLogo -noColor` runs the xUnit executable directly and prints failures. This direct runner uses `-class` / `-class-` / `-method`, not the MTP `--filter-*` syntax above.

> **Temporary generated projects**: setting `TMPDIR` inside this repository makes the HTTP generated-client tests' temporary consumer projects inherit ancestor `Directory.Packages.props`; the reviewed tests then fail with NU1008. This is separate from ASM code generation. Do not change package security/settings to hide it.

> **Compile report scope**: `SPICE86_GENERATED_CODE_COVERAGE` only compiles dumps; it does not execute them or enforce golden coverage. Rows represent emulator lifetimes, not unique ASM scenarios. A report with zero compile errors is not proof of generated-mode behavioral coverage.

> **Long-running test rule (Kiro agents only)**: the foreground shell tool (`execute_bash`) enforces a hard wall-clock cap (roughly 25-60s in practice) that fires *before* the `timeout` argument. When it trips it force-returns exit 1 while the detached `dotnet` process keeps running and completes normally, which looks exactly like a crash (abrupt exit 1, no test summary, no dump) but is not one. Any test run expected to exceed ~25s (the full suite is ~2 min; `CfgGraphReloadTest` alone is ~55s) MUST be launched with the background-process tool (which has no such cap), writing a completion marker, then polled with short (<20s) foreground calls until the marker appears. Do not conclude a suite "crashed" from a bare exit 1 with no summary; re-run it in the background and check the real result first. Splitting the run with `--filter-class` so each foreground run stays under the cap also works.

> **ASM golden regeneration rule**: normal test runs must never write committed goldens. The three source-tree writer calls (`MachineGoldens.WriteExpectedListing`, `MachineGoldens.WriteExpectedCfgJson`, and `GeneratedCodeMachineTestRunner.WriteExpectedGeneratedSource`) stay commented out except during deliberate regeneration. To regenerate, enable all three writers, rebuild, run the catalogue command below twice serially, require the second pass to make no changes, disable the writers again, rebuild, and review the complete golden diff. Regenerate files instead of editing them by hand.
>
> Machine goldens use `MachineGoldens.GoldenKey(fixture)` and live in `DumpedListing`, `DumpedCfgBlocks`, and `DumpedCfgPartitions`. Generated-source goldens use `GeneratedCodeMachineTestRunner.GoldenKey(settings)` and live in `GeneratedCode`; this key intentionally omits the scenario variant because several executions may share one generated program. `AsmFixtureGoldenCoverageTest` derives the expected file sets for both key spaces from the scenario catalogue and rejects missing and orphan files.
>
> Run regeneration serially because several tests can address the same golden (`--maximum-parallel-threads` is not a valid option for this runner):
>
> ```text
> dotnet test tests/Spice86.Tests --no-build -- --filter-class '*GeneratedCodeScenarioTest*' --filter-class '*GeneratedCodeMachineTest*' --filter-class '*SpeculativeCfgTest*' --parallel none
> ```
>
> `GeneratedCodeScenarioTest` covers the catalogue. `GeneratedCodeMachineTest` and `SpeculativeCfgTest` retain focused engine and speculative cases. Treat the generated diff as the primary review artifact; passing tests alone do not prove that regenerated expectations are correct.

### Debugging Workflow
- **GDB Integration**: Server runs on port 10000 by default (`--GdbPort 10000`)
  - Use `--Debug` to pause at startup for breakpoint setup
  - Custom GDB commands via `monitor` (e.g., `monitor dumpall`, `monitor breakCycles 1000`)
- **Seer Client**: Use `seergdb --project doc/spice86.seer` for GUI debugging
- **Internal Debugger**: UI-based debugger with disassembly, memory, CPU state views

### Reverse Engineering Process
1. Run DOS program in Spice86
2. Emulator dumps `spice86dumpMemoryDump.bin` and `spice86dumpExecutionFlow.json` to `--RecordedDataDirectory`
3. Load dumps in Ghidra via [spice86-ghidra-plugin](https://github.com/OpenRakis/spice86-ghidra-plugin)
4. Generate C# override classes from decompiled functions
5. Implement `IOverrideSupplier` to register overrides at segmented addresses
6. Run with `--UseCodeOverride true` to replace assembly with C# incrementally

## Code Override System

### Override Registration Pattern
```csharp
public class MyOverrideSupplier : IOverrideSupplier {
    public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(
        ILoggerService loggerService, Configuration configuration, 
        ushort programStartSegment, Machine machine) {
        return new MyOverrides(new(), machine, loggerService, configuration).FunctionInformations;
    }
}

public class MyOverrides : CSharpOverrideHelper {
    public MyOverrides(IDictionary<SegmentedAddress, FunctionInformation> funcInfos,
                       Machine machine, ILoggerService logger, Configuration config)
        : base(funcInfos, machine, logger, config) {
        DefineFunction(0xF000, 0xA1E8, MyFunction_F000_A1E8_FA1E8);
        OverrideInstruction(0xF000, 0xFFF0, MyInstructionOverride);
        DoOnTopOfInstruction(0xF000, 0xFFF3, MyInstructionHook);
    }
    
    public Action MyFunction_F000_A1E8_FA1E8(int loadOffset) {
        // C# implementation
        return NearRet(); // or FarRet(), FarJump(), etc.
    }
}
```

Hand-written overrides registered with `DefineFunction` hand control back to the interpreter at whatever CS:IP their action leaves. Generated code registers with `DefineFunctionWithoutInterpreterFallback`, whose transfers must land on another override or the expected return address and never fall back to the interpreter.

### CSharpOverrideHelper Capabilities
- Access CPU state via `State`, `Stack`, `Memory`, `UInt8/16/32` indexers
- Control flow: `NearRet()`, `FarRet()`, `NearCall()`, `FarCall()`, `FarJump()`, `Hlt()`
- Jump dispatching for computed/self-modifying jumps via `JumpDispatcher`
- See `tests/Spice86.Tests/CSharpOverrideHelperTest.cs` for patterns

### Memory Data Structures
Wrap memory regions in typed accessors:
```csharp
public class GlobalsOnDs : MemoryBasedDataStructureWithDsBaseAddress {
    public GlobalsOnDs(IByteReaderWriter memory, SegmentRegisters segmentRegisters) 
        : base(memory, segmentRegisters) { }
    
    public int GetDialogueCount47A8() => UInt16[0x47A8];
    public void SetDialogueCount47A8(int value) => UInt16[0x47A8] = (ushort)value;
}
```
Variants: `MemoryBasedDataStructureWithCsBaseAddress`, `MemoryBasedDataStructureWithSsBaseAddress`, etc.

## Project-Specific Conventions

### Critical AI Agent Guidelines
- **This is a C# project** - never suggest Python solutions
- **No scripts outside the project** - do NOT create temporary scripts (Python, Bash, PowerShell, Node, etc.) anywhere on the filesystem, including `/tmp`, the user's home directory, or any location outside the workspace. Do not create throwaway helper scripts inside the workspace either. Use the available tools (file editing, grep, search, terminal one-liners) directly. If a multi-step computation is truly needed, run it as an inline shell one-liner in the terminal without writing a file.
- **Use `tmp/` for temporary files** - if a temporary file must be written (e.g., captured command output, intermediate data), place it inside the `tmp/` folder at the root of the repository. Never write to `/tmp` or any path outside the workspace.
- **Avoid complexity** - keep cyclomatic complexity low, prefer simple, linear code over nested conditionals
- **No optional parameters** - avoid optional/defaulted method parameters in new code. Prefer non-nullable types; a field or dependency should be nullable only when its absence is a real, distinct state (e.g. a feature disabled by a flag). When a nullable feature-gate would otherwise force `?.`/`is not null` guards across many call sites, prefer a null-object (no-op) implementation or unconditional construction over a nullable reference.
- **No complex ternary expressions** - avoid nested, chained, or multi-line ternaries; simple single-line ternaries with short operands are allowed (e.g. `int x = a > b ? a : b;`), but non-trivial conditions or non-trivial branches must use explicit `if/else`
- **Minimal comments** - write self-documenting code with clear names; avoid obvious comments
- **No references to plan/spec sections in code** - never point comments, XML docs, or test names at sections of a plan, spec, or implementation-tracking document (e.g. "Phase 5", "Gap D", "Rule 4", "section S6.1.1", "Test 18", "implementation plan"). Those documents are temporary or drift over time, leaving dangling pointers that mean nothing to a future reader. Describe the actual behavior, invariant, or scenario directly instead.
- **Test before submit** - always run tests after code changes to verify functionality
- **Rebuild and verify** - For any task that changes code or tests, rebuild the project and run the full test suite; do not stop until all tests are green.
- **Concise documentation** - XML docs should be precise and complete but not verbose; avoid excessive remarks
- **Ignore Machine class** - this is a legacy aggregator class; work directly with specific components (`CfgCpu`, `Memory`, `Stack`, etc.) instead
- **Enforce TDD** - ensure integration tests are present and not passing at first, then make them pass by updating the implementation.
- **Clear test code style** use explicit Arrange/Act/Assert structure, avoid long setup blocks, and reduce duplication with small helpers.
- **Pause-refresh rule for new ViewModels** - view models that refresh from a dispatcher timer must refresh at most once per pause cycle. Gate updates on `IPauseHandler.IsPaused`, track a per-pause boolean flag, and reset it on resume.

### Avalonia Telemetry
- **Avalonia telemetry must be disabled** when working on the codebase.
- The repository already has telemetry disabled in code, but it may still cause issues for AI agents.
- Be aware of this configuration to avoid telemetry-related blocking issues.

### Code Style (enforced by `.editorconfig`)
- **No `var` keyword**: Use explicit types instead (enforced by `.editorconfig`)
  ```csharp
  // Wrong
  var count = 10;
  
  // Correct
  int count = 10;
  ```
- **One top-level type per file**: Do not place multiple classes/structs/enums in the same file
  - Exception: private nested/inner types declared inside a class are allowed
  - Group related types via namespaces, not by co-locating multiple top-level types
- **No generic catch clauses**: Catch specific exceptions only - **this is strictly enforced**
  ```csharp
  // WRONG - NEVER DO THIS
  try {
      // code
  } catch (Exception ex) {
      // handling
  }
  
  // CORRECT - Catch specific exceptions
  try {
      // code
  } catch (IOException ex) {
      // handling
  } catch (ArgumentException ex) {
      // handling
  }
  ```
  - **NEVER use generic `catch (Exception)`, `catch (Exception e)`, or empty `catch`**
  - Each exception type must be caught explicitly
  - This is non-negotiable - the .editorconfig enforces this rule
  
- **No null-forgiving operator (!)**: The null-forgiving operator is **BANNED**
  ```csharp
  // Wrong - NEVER use !
  string value = nullable!.ToString();
  
  // Correct
  if (nullable != null) {
      string value = nullable.ToString();
  }
  // Or
  string value = nullable?.ToString() ?? "default";
  ```
  - Properly handle null cases with null checks, null-coalescing, or null-conditional operators
  - Don't ignore nullable warnings - fix the underlying issue
  - **Avoid non-ASCII Unicode characters**: Prefer plain ASCII in source code and documentation; only use Unicode when necessary for languages that require characters outside the ASCII range (for example, Chinese). Some editors or tools may not assume UTF-8 and can render or save these characters incorrectly.
- **Do not use `#region`**: Avoid `#region`/`#endregion` blocks; keep code organized via clear structure and namespaces
- **Do not suppress warnings with pragmas**: Never disable warnings using preprocessor directives (e.g., `#pragma warning disable`). Fix the underlying issue instead.
- **Use `Path.Join`, not `Path.Combine`**: Never use `System.IO.Path.Combine`. It silently discards all earlier segments if a later argument is rooted, and CodeQL flags every call ("Path.Combine may silently drop its earlier arguments") because it cannot prove the arguments are relative. Always use `Path.Join`, which concatenates segments without dropping any. This is a very frequent CodeQL finding - prefer `Path.Join` everywhere paths are assembled.
- **No complex ternary operator**: Avoid nested, chained, or multi-line ternary expressions. Simple single-line ternaries with short, obvious operands are acceptable. Use explicit `if/else` blocks when the condition or either branch is non-trivial.
- **Async usage restrictions**:
  - **Do NOT let async "infect" the `Spice86.Core` assembly**
  - Keep async code in the UI layer (`Spice86` project) only
  - For the UI, use Dispatcher-based timer updates (mainly used on pause)
  - **Never use sync-over-async patterns** (`Task.Result`, `Task.Wait()`, `GetAwaiter().GetResult()`) in production code or tests
  - Propagate async flow end-to-end when needed instead of blocking threads
  - This separation maintains clean architecture boundaries
- **Brace style**: Java-style (opening brace on same line)
  ```csharp
  if (condition) {
      // code
  }
  ```
- **Namespaces**: File-scoped only
  ```csharp
  namespace Spice86.Core.Emulator.CPU;
  ```
- **Nullable**: Enabled project-wide with `<WarningsAsErrors>nullable</WarningsAsErrors>`
- **Documentation**: XML comments required (`<GenerateDocumentationFile>true</GenerateDocumentationFile>`)
- **No stub implementations**: Create proper real implementations
  - Stubs that return hardcoded values or do nothing are not allowed
  - Magic values are not allowed, define enums or consts with clear names

### Testing Patterns
- **Prefer ASM-based tests over unit tests** for testing the emulator
  - Unit tests are acceptable for interrupt handlers that don't override `WriteAssemblyInRam` and deal with few dependencies
  - Use assembly-based integration tests for comprehensive emulator validation
- **Every runnable ASM scenario is catalogue-driven**
  - Register it in `tests/Spice86.Tests/AsmFixtures/AsmScenarioCatalog.cs` with one behavioral oracle. Keep child programs, overlays, includes, and data files as dependencies of the scenario that loads them, not as standalone scenarios.
  - `GeneratedCodeScenarioTest.CatalogueCaseRunsAsGeneratedCode` derives its cases from the catalogue. It performs interpreted discovery, checks the machine goldens, generates and compiles C#, executes each registered variant with the same oracle, and checks the generated-source golden. Adding a scenario therefore adds generated coverage automatically.
  - `MachineTest`, `GeneratedCodeMachineTest`, and `SpeculativeCfgTest` retain focused engine, speculative, and fault-injection checks. They are not the registration source of truth.
  - Follow the golden-key and regeneration rules above. Do not force machine-golden and generated-source keys to match, and do not add exceptions for missing or orphan files.
- Use FluentAssertions for assertions: `result.Should().Be(expected)`
- Mock with NSubstitute: `Substitute.For<IInterface>()`
- CPU tests in `tests/Spice86.Tests/CpuTests/` use SingleStepTests NuGet packages for validation

### Logging
- Inject `ILoggerService`, check log level before expensive operations:
  ```csharp
  if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
      _loggerService.Verbose("Details: {Value}", expensiveOperation());
  }
  ```
- Log levels controlled by CLI: `--VerboseLogs`, `--WarningLogs`, `--SilencedLogs`

## Key Integration Points

### External Dependencies
- **Avalonia**: Cross-platform UI framework (MVVM pattern)
- **PortAudio**: Audio output via `Bufdio.Spice86` (requires `libportaudio` on Unix)
- **Serilog**: Structured logging with console/debug/file sinks
- **Morris.Moxy**: Code generation for CPU instruction parsing mixins
- **CommandLineParser**: CLI argument parsing into `Configuration` class

### Hardware Emulation Entry Points
- Memory access: `IMemory` interface in `Spice86.Core/Emulator/Memory/`
- I/O ports: `IOPortDispatcher` routes port reads/writes to device handlers
- Interrupts: `InterruptVectorTable` + handlers in `InterruptHandlers/` (BIOS/DOS/Timer/Input)
- Timer: `Timer` class wraps Intel 8254 PIT with three counters
- Video: `VgaCard` + `Renderer` for VGA/EGA/CGA modes

### Cross-Component Communication
- **GDB Protocol**: `Spice86.Core/Emulator/Gdb/` implements remote debugging protocol
- **Breakpoints**: `EmulatorBreakpointsManager` coordinates memory/IO/execution breakpoints
- **Pause Handling**: `IPauseHandler` allows pausing/resuming from UI or debugger
- **Function Tracking**: `FunctionHandler` intercepts calls/rets for CFG building and override dispatch

## Common Gotchas
- **Segmented addressing**: Use `SegmentedAddress` not raw offsets; linear address = segment * 16 + offset. Avoid computing addresses from `SegmentedAddress.Linear` because segmented addresses can roll over and `.Linear` does not handle this correctly. Use the `IMmu` (`TranslateAddress`) for address translation instead, as it handles rollover and access validation. Passing `.Linear` to an API that already expects a linear address is fine.
- **A20 Gate**: Memory wrapping at 1MB boundary controlled by `A20Gate` (toggle via `--A20Gate` flag)
- **EMS/XMS**: Enabled by default; disable with `--Xms false` / `--Ems false`
- **Time handling**: Real-time vs instruction-based via `--InstructionTimeScale` or `--TimeMultiplier`
- **Internal visibility**: `Spice86.Tests` has `InternalsVisibleTo` for testing internal APIs

## Reference Examples
- Override system: `tests/Spice86.Tests/CSharpOverrideHelperTest.cs`
- DI setup: `src/Spice86/Spice86DependencyInjection.cs`
- CFG CPU: `src/Spice86.Core/Emulator/CPU/CfgCpu/` + `doc/cfgcpuReadme.md`
- Real-world usage: [Cryogenic project](https://github.com/OpenRakis/Cryogenic) (Dune rewrite)
