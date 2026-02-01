# Copilot Instructions for Spice86

## Project Overview
Spice86 is a .NET 8 cross-platform emulator for reverse engineering real-mode DOS programs. It enables running, analyzing, and incrementally rewriting DOS binaries in C# without source code.

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
```powershell
# Build from solution root
dotnet build

# Run with executable
dotnet run --project src/Spice86 -- -e path\to\program.exe

# Run tests
dotnet test tests/Spice86.Tests
```

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
- **Avoid complexity** - keep cyclomatic complexity low, prefer simple, linear code over nested conditionals
- **No optional parameters** - avoid nullable or optional parameters in new code
- **Minimal comments** - write self-documenting code with clear names; avoid obvious comments
- **Test before submit** - always run tests after code changes to verify functionality
- **Concise documentation** - XML docs should be precise and complete but not verbose; avoid excessive remarks
- **Ignore Machine class** - this is a legacy aggregator class; work directly with specific components (`CfgCpu`, `Memory`, `Stack`, etc.) instead

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
- **Do not use `#region`**: Avoid `#region`/`#endregion` blocks; keep code organized via clear structure and namespaces
- **Do not suppress warnings with pragmas**: Never disable warnings using preprocessor directives (e.g., `#pragma warning disable`). Fix the underlying issue instead.
- **Async usage restrictions**:
  - **Do NOT let async "infect" the `Spice86.Core` assembly**
  - Keep async code in the UI layer (`Spice86` project) only
  - For the UI, use Dispatcher-based timer updates (mainly used on pause)
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
- **Segmented addressing**: Use `SegmentedAddress` not raw offsets; linear address = segment * 16 + offset
- **A20 Gate**: Memory wrapping at 1MB boundary controlled by `A20Gate` (toggle via `--A20Gate` flag)
- **EMS/XMS**: Enabled by default; disable with `--Xms false` / `--Ems false`
- **Time handling**: Real-time vs instruction-based via `--InstructionsPerSecond` or `--TimeMultiplier`
- **Internal visibility**: `Spice86.Tests` has `InternalsVisibleTo` for testing internal APIs

## Reference Examples
- Override system: `tests/Spice86.Tests/CSharpOverrideHelperTest.cs`
- DI setup: `src/Spice86/Spice86DependencyInjection.cs`
- CFG CPU: `src/Spice86.Core/Emulator/CPU/CfgCpu/` + `doc/cfgcpuReadme.md`
- Real-world usage: [Cryogenic project](https://github.com/OpenRakis/Cryogenic) (Dune rewrite)
