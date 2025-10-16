# Copilot Instructions for Spice86

## Project Overview
Spice86 is a .NET 8 cross-platform emulator for reverse engineering real mode DOS programs. It enables running, analyzing, and incrementally rewriting DOS binaries in C# without source code.

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
- Machine parts are wired together with event handlers and shared state
- Entry point is `Program.cs` which instantiates `Spice86DependencyInjection`

### CPU Execution Models
Two CPU implementations coexist via `IInstructionExecutor`:
- **`Cpu`**: Traditional interpreter with instruction-by-instruction execution
- **`CfgCpu`**: Control Flow Graph-based executor that builds dynamic CFG for analysis and future JIT
  - Tracks instruction variants for self-modifying code via selector nodes
  - Maintains execution context hierarchy for hardware interrupts
  - See `doc/cfgcpuReadme.md` for CFG architecture details
- Toggle via `--CfgCpu` flag; CfgCpu is the future direction

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
1. Run DOS program in Spice86 with `--DumpDataOnExit true`
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

### Code Style (enforced by `.editorconfig`)
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

### Testing Patterns
- Use FluentAssertions for assertions: `result.Should().Be(expected)`
- Mock with NSubstitute: `Substitute.For<IInterface>()`
- Theory tests for CPU configurations: `[Theory] [MemberData(nameof(GetCfgCpuConfigurations))]`
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
