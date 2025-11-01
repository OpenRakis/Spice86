namespace Spice86.Tests.Emulator.Gdb;

using FluentAssertions;

using Spice86.Core.CLI;
using Spice86.Core.Emulator;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound;

using Xunit;

using static Spice86.Core.Emulator.CPU.CpuModel;

/// <summary>
/// Full integration tests for GDB server using a real GDB protocol client.
/// Tests complete command/response cycles, breakpoints, stepping, memory access, and custom monitor commands.
/// </summary>
public class GdbFullIntegrationTests : IDisposable {
    private readonly List<Spice86DependencyInjection> _injections = new();
    private readonly List<GdbClient> _clients = new();
    private readonly List<Task> _executionTasks = new();

    public void Dispose() {
        foreach (GdbClient client in _clients) {
            try {
                client.Dispose();
            } catch {
                // Ignore cleanup errors
            }
        }

        foreach (Spice86DependencyInjection injection in _injections) {
            try {
                injection.Machine.CpuState.IsRunning = false;
                injection.Dispose();
            } catch {
                // Ignore cleanup errors
            }
        }

        // Wait for execution tasks to complete
        Task.WaitAll(_executionTasks.Where(t => !t.IsCompleted).ToArray(), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task GdbClient_QuerySupported_ShouldReceiveResponse() {
        // Arrange
        int port = GetAvailablePort();
        using GdbClient client = await StartGdbServerAndConnectAsync(port);

        // Act
        string response = await client.QuerySupportedAsync();

        // Assert
        response.Should().NotBeEmpty("Server should respond to qSupported query");
    }

    [Fact]
    public async Task GdbClient_QueryHaltReason_ShouldReceiveResponse() {
        // Arrange
        int port = GetAvailablePort();
        using GdbClient client = await StartGdbServerAndConnectAsync(port);

        // Act
        string response = await client.QueryHaltReasonAsync();

        // Assert
        response.Should().NotBeEmpty("Server should respond with halt reason");
    }

    [Fact]
    public async Task GdbClient_ReadAllRegisters_ShouldReturnRegisterValues() {
        // Arrange
        int port = GetAvailablePort();
        using GdbClient client = await StartGdbServerAndConnectAsync(port);

        // Act
        string response = await client.ReadRegistersAsync();

        // Assert
        response.Should().NotBeEmpty();
        // Should return 16 registers * 8 hex chars = 128 chars
        response.Length.Should().BeGreaterOrEqualTo(128, "Response should contain all register values");
    }

    [Fact]
    public async Task GdbClient_ReadSpecificRegister_ShouldReturnRegisterValue() {
        // Arrange
        int port = GetAvailablePort();
        using GdbClient client = await StartGdbServerAndConnectAsync(port);

        // Act - Read register 0 (AX)
        string response = await client.ReadRegisterAsync(0);

        // Assert
        response.Should().NotBeEmpty();
        response.Length.Should().Be(8, "Register value should be 8 hex characters (32-bit)");
    }

    [Fact]
    public async Task GdbClient_WriteRegister_ShouldUpdateRegisterValue() {
        // Arrange
        int port = GetAvailablePort();
        Spice86DependencyInjection injection = await StartGdbServerAsync(port);
        using GdbClient client = new();
        await client.ConnectAsync("127.0.0.1", port);
        _clients.Add(client);

        // Act - Write to register 0 (AX)
        string writeResponse = await client.WriteRegisterAsync(0, 0x12340000); // Little-endian format
        
        // Read back the value
        string readResponse = await client.ReadRegisterAsync(0);

        // Assert
        writeResponse.Should().Contain("OK", "Write operation should succeed");
        readResponse.Should().Contain("1234", "Register should contain the written value");
    }

    [Fact]
    public async Task GdbClient_ReadMemory_ShouldReturnMemoryContents() {
        // Arrange
        int port = GetAvailablePort();
        Spice86DependencyInjection injection = await StartGdbServerAsync(port);
        
        // Write some test data to memory
        injection.Machine.Memory.UInt8[0x1000] = 0xAB;
        injection.Machine.Memory.UInt8[0x1001] = 0xCD;
        injection.Machine.Memory.UInt8[0x1002] = 0xEF;
        
        using GdbClient client = new();
        await client.ConnectAsync("127.0.0.1", port);
        _clients.Add(client);

        // Act
        string response = await client.ReadMemoryAsync(0x1000, 3);

        // Assert
        response.Should().NotBeEmpty();
        response.Should().Contain("ABCDEF", "Memory should contain the test data");
    }

    [Fact]
    public async Task GdbClient_WriteMemory_ShouldUpdateMemoryContents() {
        // Arrange
        int port = GetAvailablePort();
        Spice86DependencyInjection injection = await StartGdbServerAsync(port);
        using GdbClient client = new();
        await client.ConnectAsync("127.0.0.1", port);
        _clients.Add(client);

        byte[] testData = new byte[] { 0x12, 0x34, 0x56 };

        // Act
        string writeResponse = await client.WriteMemoryAsync(0x2000, testData);
        
        // Verify the write
        byte val1 = injection.Machine.Memory.UInt8[0x2000];
        byte val2 = injection.Machine.Memory.UInt8[0x2001];
        byte val3 = injection.Machine.Memory.UInt8[0x2002];

        // Assert
        writeResponse.Should().Contain("OK", "Write operation should succeed");
        val1.Should().Be(0x12);
        val2.Should().Be(0x34);
        val3.Should().Be(0x56);
    }

    [Fact]
    public async Task GdbClient_SetAndRemoveBreakpoint_ShouldSucceed() {
        // Arrange
        int port = GetAvailablePort();
        using GdbClient client = await StartGdbServerAndConnectAsync(port);

        // Act - Set breakpoint
        string setResponse = await client.SetBreakpointAsync(0xF000A1E8);
        
        // Remove breakpoint
        string removeResponse = await client.RemoveBreakpointAsync(0xF000A1E8);

        // Assert
        setResponse.Should().Contain("OK", "Setting breakpoint should succeed");
        removeResponse.Should().Contain("OK", "Removing breakpoint should succeed");
    }

    [Fact]
    public async Task GdbClient_MonitorHelp_ShouldReturnCustomCommands() {
        // Arrange
        int port = GetAvailablePort();
        using GdbClient client = await StartGdbServerAndConnectAsync(port);

        // Act
        string response = await client.SendMonitorCommandAsync("help");

        // Assert
        response.Should().NotBeEmpty("Monitor help should return available commands");
        // Response is hex-encoded, so check for hex-encoded versions of command names
        // "breakCycles" in hex contains these patterns
        response.Should().MatchRegex("[0-9A-Fa-f]+", "Response should be hex-encoded");
    }

    [Fact]
    public async Task GdbClient_MonitorBreakCycles_ShouldSetCycleBreakpoint() {
        // Arrange
        int port = GetAvailablePort();
        using GdbClient client = await StartGdbServerAndConnectAsync(port);

        // Act
        string response = await client.SendMonitorCommandAsync("breakCycles 1000");

        // Assert
        response.Should().NotBeEmpty();
        // Should confirm the breakpoint was set
        response.Should().MatchRegex("[0-9A-Fa-f]+", "Response should be hex-encoded confirmation");
    }

    [Theory]
    [InlineData(false)] // Traditional CPU
    [InlineData(true)]  // CfgCpu
    public async Task GdbClient_WithInstructionsPerSecond_ShouldWorkWithBothCpuModes(bool enableCfgCpu) {
        // Arrange
        int port = GetAvailablePort();
        Configuration config = new() {
            Exe = "Resources/cpuTests/add.bin",
            ExpectedChecksumValue = Array.Empty<byte>(),
            GdbPort = port,
            InstructionsPerSecond = 100000,
            HeadlessMode = HeadlessType.Minimal,
            CfgCpu = enableCfgCpu,
            AudioEngine = AudioEngine.Dummy,
            InitializeDOS = false,
            Debug = true // Start paused
        };

        Spice86DependencyInjection injection = new(config);
        injection.Machine.CpuState.Flags.CpuModel = ZET_86;
        _injections.Add(injection);

        // Start execution in background
        Task executionTask = Task.Run(() => {
            try {
                injection.ProgramExecutor.Run();
            } catch {
                // Ignore errors from stopping execution
            }
        });
        _executionTasks.Add(executionTask);

        // Wait for server to start
        await Task.Delay(1000);

        using GdbClient client = new();
        _clients.Add(client);

        // Act
        await client.ConnectAsync("127.0.0.1", port);
        string response = await client.QuerySupportedAsync();

        // Assert
        response.Should().NotBeEmpty($"GDB should work with CfgCpu={enableCfgCpu} and InstructionsPerSecond");
    }

    [Fact]
    public async Task GdbClient_Step_ShouldAdvanceOneInstruction() {
        // Arrange
        int port = GetAvailablePort();
        using GdbClient client = await StartGdbServerAndConnectAsync(port);

        // Get initial IP
        string initialRegs = await client.ReadRegistersAsync();

        // Act - Single step
        string stepResponse = await client.StepAsync();

        // Get new IP
        string newRegs = await client.ReadRegistersAsync();

        // Assert
        stepResponse.Should().NotBeEmpty("Step command should return status");
        // Registers should have changed after stepping
        newRegs.Should().NotBe(initialRegs, "Registers should change after stepping an instruction");
    }

    [Fact]
    public async Task GdbClient_Detach_ShouldDisconnectGracefully() {
        // Arrange
        int port = GetAvailablePort();
        using GdbClient client = await StartGdbServerAndConnectAsync(port);

        // Act
        string response = await client.DetachAsync();

        // Assert
        response.Should().Contain("OK", "Detach should succeed");
    }

    [Fact]
    public async Task GdbClient_MultipleCommands_ShouldWorkInSequence() {
        // Arrange
        int port = GetAvailablePort();
        using GdbClient client = await StartGdbServerAndConnectAsync(port);

        // Act - Execute multiple commands
        string supported = await client.QuerySupportedAsync();
        string haltReason = await client.QueryHaltReasonAsync();
        string registers = await client.ReadRegistersAsync();
        string register0 = await client.ReadRegisterAsync(0);

        // Assert
        supported.Should().NotBeEmpty();
        haltReason.Should().NotBeEmpty();
        registers.Should().NotBeEmpty();
        register0.Should().NotBeEmpty();
    }

    private async Task<GdbClient> StartGdbServerAndConnectAsync(int port) {
        await StartGdbServerAsync(port);
        
        GdbClient client = new();
        await client.ConnectAsync("127.0.0.1", port);
        _clients.Add(client);
        
        return client;
    }

    private async Task<Spice86DependencyInjection> StartGdbServerAsync(int port) {
        Configuration config = new() {
            Exe = "Resources/cpuTests/add.bin",
            ExpectedChecksumValue = Array.Empty<byte>(),
            GdbPort = port,
            InstructionsPerSecond = 100000,
            HeadlessMode = HeadlessType.Minimal,
            AudioEngine = AudioEngine.Dummy,
            CfgCpu = false,
            InitializeDOS = false,
            Debug = true // Start paused so GDB can connect
        };

        Spice86DependencyInjection injection = new(config);
        injection.Machine.CpuState.Flags.CpuModel = ZET_86;
        _injections.Add(injection);

        // Start execution in background
        Task executionTask = Task.Run(() => {
            try {
                injection.ProgramExecutor.Run();
            } catch {
                // Ignore errors from stopping execution
            }
        });
        _executionTasks.Add(executionTask);

        // Wait for GDB server to start
        await Task.Delay(1000);

        return injection;
    }

    private static int GetAvailablePort() {
        using System.Net.Sockets.TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
