# GDB Testing Infrastructure

This directory contains comprehensive unit and integration tests for Spice86's GDB remote debugging protocol support.

## Overview

The GDB server in Spice86 allows developers to debug emulated DOS programs using standard GDB clients. These tests ensure:
- GDB protocol compliance
- Proper register and memory access
- InstructionsPerSecond mode compatibility
- Prevention of regressions in GDB support

## Test Structure

### Unit Tests

#### `GdbFormatterTests.cs`
Tests the GDB protocol value formatting utilities:
- **FormatValueAsHex32**: Tests 32-bit value formatting with little-endian byte swapping
- **FormatValueAsHex8**: Tests 8-bit value formatting

These tests ensure values are correctly formatted for the GDB remote protocol.

#### `GdbIoTests.cs`
Tests the GDB protocol I/O layer:
- **GenerateResponse**: Validates protocol message formatting (+$data#checksum)
- **GenerateMessageToDisplayResponse**: Tests hex-encoded message generation
- **Checksum calculation**: Verifies correct 8-bit checksum computation

#### `GdbCommandRegisterHandlerTests.cs`
Tests CPU register access via GDB commands:
- **ReadAllRegisters**: Tests reading all 16 x86 registers (AX-DI, IP, FLAGS, segment registers)
- **ReadRegister**: Tests reading individual registers
- **WriteRegister**: Tests writing to individual registers
- **WriteAllRegisters**: Tests bulk register updates
- **Invalid input handling**: Ensures graceful error responses

#### `GdbCommandMemoryHandlerTests.cs`
Tests memory access via GDB commands:
- **Invalid input handling**: Verifies proper error handling for malformed commands

### Integration Tests

#### `GdbServerIntegrationTests.cs`
Tests GDB server initialization and configuration:
- **Spice86_WithGdbPortConfigured**: Verifies GDB server creation when port is set
- **Spice86_WithGdbPortZero**: Confirms GDB is disabled when port is 0
- **Spice86_WithInstructionsPerSecond**: Tests both CPU modes (standard and CfgCpu) with InstructionsPerSecond
- **Spice86_InHeadlessMode**: Validates GDB works in minimal headless mode

## Running the Tests

Run all GDB tests:
```bash
dotnet test tests/Spice86.Tests/Spice86.Tests.csproj --filter "FullyQualifiedName~Gdb"
```

Run specific test class:
```bash
dotnet test --filter "FullyQualifiedName~GdbFormatterTests"
```

## Why These Tests Matter

### InstructionsPerSecond Mode
GDB users rely on InstructionsPerSecond mode for deterministic debugging. The timer in instruction-counting mode (vs real-time) ensures:
- Breakpoints fire consistently
- Single-stepping is predictable
- Debugging doesn't race against real-time timers

The tests verify this mode works with both traditional and CfgCpu execution engines.

### Regression Prevention
GDB support has regressed multiple times in the past. These tests catch:
- Protocol format changes
- Register mapping errors
- Memory access issues
- Configuration problems

## Future Enhancements

### Full Integration Testing with GDB Client

The current integration tests verify server initialization but don't exercise the full GDB protocol with an actual client. Future enhancements should include:

1. **True Integration Tests**: Start the GDB server, connect a real GDB client (or implement a minimal GDB client in C#), and test:
   - Complete command/response cycles
   - Breakpoint setting and triggering
   - Memory watchpoints
   - Register modifications
   - Stepping through instructions
   - Custom `monitor` commands

2. **Test Coverage for All Commands**: The GDB server implements many commands that aren't yet unit tested:
   - `qSupported` negotiation
   - `vCont` continuation commands
   - `Z`/`z` breakpoint operations
   - `qRcmd` custom monitor commands (dumpall, breakCycles, etc.)
   - Thread operations

3. **Headless Avalonia Mode Testing**: Currently tests only use HeadlessType.Minimal. Should test HeadlessType.Avalonia as well.

4. **UI Mode Testing**: Test GDB integration with full UI enabled (requires UI testing framework).

### Implementation Approach

To enable full integration testing, consider:

1. **Expose GDB Server**: Make `ProgramExecutor._gdbServer` accessible for testing (via internal visibility or test-specific interface)

2. **Use .NET GDB Client Library**: If available, or implement a minimal GDB RSP client:
   ```csharp
   class GdbTestClient {
       TcpClient _client;
       
       public string SendCommand(string cmd) {
           // Calculate checksum, send $cmd#checksum
           // Read and parse response
       }
       
       public void SetBreakpoint(uint address) => SendCommand($"Z0,{address:X},1");
       public Dictionary<string, uint> ReadRegisters() { ... }
   }
   ```

3. **Integration Test Pattern**:
   ```csharp
   [Fact]
   public async Task GdbServer_SetBreakpoint_ShouldTriggerOnExecution() {
       // Start emulator with GDB enabled
       using var emulator = CreateTestEmulator(gdbPort: 10000);
       await emulator.StartAsync();
       
       // Connect GDB client
       using var gdbClient = new GdbTestClient("localhost", 10000);
       
       // Set breakpoint at specific address
       gdbClient.SetBreakpoint(0xF000_A1E8);
       
       // Continue execution
       gdbClient.Continue();
       
       // Verify breakpoint triggered
       var status = await gdbClient.WaitForStop(timeout: TimeSpan.FromSeconds(5));
       status.Reason.Should().Be(StopReason.Breakpoint);
       status.Address.Should().Be(0xF000_A1E8);
   }
   ```

4. **Monitor Command Testing**:
   ```csharp
   [Fact]
   public void GdbServer_MonitorHelp_ShouldReturnCommandList() {
       using var client = new GdbTestClient("localhost", gdbPort);
       string response = client.SendMonitorCommand("help");
       response.Should().Contain("breakCycles");
       response.Should().Contain("dumpall");
       response.Should().Contain("breakStop");
   }
   ```

## Test Data

Tests use minimal test binaries from `Resources/cpuTests/`:
- `add.bin`: Simple arithmetic operation test
- Tests run with ZET_86 CPU model for compatibility
- No DOS interrupt vectors by default (InitializeDOS = false)
- Dummy audio engine to avoid PortAudio dependencies

## Related Documentation

- [GDB Remote Protocol](https://sourceware.org/gdb/current/onlinedocs/gdb.html/Remote-Protocol.html)
- [Spice86 GDB Usage](../../../README.md#dynamic-analysis)
- [Seer GDB Client Configuration](../../../doc/spice86.seer)

## Contributing

When adding GDB features:
1. Add unit tests for new command handlers
2. Add integration tests for configuration changes
3. Update this README with new test descriptions
4. Ensure all tests pass before submitting PR
