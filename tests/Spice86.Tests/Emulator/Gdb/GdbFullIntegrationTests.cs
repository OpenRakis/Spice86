namespace Spice86.Tests.Emulator.Gdb;

using FluentAssertions;

using Spice86.Core.CLI;
using Spice86.Core.Emulator;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound;

using Xunit;

using static Spice86.Core.Emulator.CPU.CpuModel;

/// <summary>
/// Full integration tests for GDB server using a real GDB protocol client in a separate process.
/// Tests complete command/response cycles, breakpoints, stepping, memory access, and custom monitor commands.
/// </summary>
[Collection("GDB Integration Tests")]
public class GdbFullIntegrationTests : IDisposable {
    private readonly List<Spice86DependencyInjection> _injections = new();
    private readonly List<GdbClientProcess> _clients = new();
    private readonly List<Task> _executionTasks = new();

    public void Dispose() {
        foreach (GdbClientProcess client in _clients) {
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

        // Don't wait for execution tasks - they'll be terminated by IsRunning = false
    }

    [Fact]
    public async Task GdbClient_QuerySupported_ShouldReceiveResponse() {
        // Arrange
        int port = GetAvailablePort();
        GdbClientProcess client = await StartGdbServerAndConnectAsync(port);

        // Act
        string response = await client.SendCommandAsync("qSupported");

        // Assert
        response.Should().NotBeEmpty("Server should respond to qSupported query");
    }

    [Fact]
    public async Task GdbClient_QueryHaltReason_ShouldReceiveResponse() {
        // Arrange
        int port = GetAvailablePort();
        GdbClientProcess client = await StartGdbServerAndConnectAsync(port);

        // Act
        string response = await client.SendCommandAsync("?");

        // Assert
        response.Should().NotBeEmpty("Server should respond with halt reason");
    }

    [Fact]
    public async Task GdbClient_ReadAllRegisters_ShouldReturnRegisterValues() {
        // Arrange
        int port = GetAvailablePort();
        GdbClientProcess client = await StartGdbServerAndConnectAsync(port);

        // Act
        string response = await client.SendCommandAsync("g");

        // Assert
        response.Should().NotBeEmpty();
        // Should return 16 registers * 8 hex chars = 128 chars
        response.Length.Should().BeGreaterOrEqualTo(128, "Response should contain all register values");
    }

    [Fact]
    public async Task GdbClient_ReadSpecificRegister_ShouldReturnRegisterValue() {
        // Arrange
        int port = GetAvailablePort();
        GdbClientProcess client = await StartGdbServerAndConnectAsync(port);

        // Act - Read register 0 (AX)
        string response = await client.SendCommandAsync("p0");

        // Assert
        response.Should().NotBeEmpty();
        response.Length.Should().Be(8, "Register value should be 8 hex characters (32-bit)");
    }

    [Fact]
    public async Task GdbClient_WriteRegister_ShouldUpdateRegisterValue() {
        // Arrange
        int port = GetAvailablePort();
        Spice86DependencyInjection injection = await StartGdbServerAsync(port);
        GdbClientProcess client = new();
        await client.StartAsync("127.0.0.1", port);
        _clients.Add(client);

        // Act - Write to register 0 (AX)
        string writeResponse = await client.SendCommandAsync("P0=12340000"); // Little-endian format
        
        // Read back the value
        string readResponse = await client.SendCommandAsync("p0");

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
        
        GdbClientProcess client = new();
        await client.StartAsync("127.0.0.1", port);
        _clients.Add(client);

        // Act
        string response = await client.SendCommandAsync("m1000,3");

        // Assert
        response.Should().NotBeEmpty();
        response.Should().Contain("ABCDEF", "Memory should contain the test data");
    }

    [Fact]
    public async Task GdbClient_WriteMemory_ShouldUpdateMemoryContents() {
        // Arrange
        int port = GetAvailablePort();
        Spice86DependencyInjection injection = await StartGdbServerAsync(port);
        GdbClientProcess client = new();
        await client.StartAsync("127.0.0.1", port);
        _clients.Add(client);

        // Act
        string writeResponse = await client.SendCommandAsync("M2000,3:123456");
        
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
        GdbClientProcess client = await StartGdbServerAndConnectAsync(port);

        // Act - Set breakpoint
        string setResponse = await client.SendCommandAsync("Z0,F000A1E8,1");
        
        // Remove breakpoint
        string removeResponse = await client.SendCommandAsync("z0,F000A1E8,1");

        // Assert
        setResponse.Should().Contain("OK", "Setting breakpoint should succeed");
        removeResponse.Should().Contain("OK", "Removing breakpoint should succeed");
    }

    [Fact]
    public async Task GdbClient_MonitorHelp_ShouldReturnCustomCommands() {
        // Arrange
        int port = GetAvailablePort();
        GdbClientProcess client = await StartGdbServerAndConnectAsync(port);

        // Act - Send monitor help command (hex-encoded "help")
        string response = await client.SendCommandAsync("qRcmd,68656c70");

        // Assert
        response.Should().NotBeEmpty("Monitor help should return available commands");
        // Response is hex-encoded, so check for hex pattern
        response.Should().MatchRegex("[0-9A-Fa-f]+", "Response should be hex-encoded");
    }

    [Fact]
    public async Task GdbClient_MonitorBreakCycles_ShouldSetCycleBreakpoint() {
        // Arrange
        int port = GetAvailablePort();
        GdbClientProcess client = await StartGdbServerAndConnectAsync(port);

        // Act - "breakCycles 1000" in hex
        string response = await client.SendCommandAsync("qRcmd,627265616b4379636c6573203130303");

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
        await Task.Delay(1500);

        GdbClientProcess client = new();
        _clients.Add(client);

        // Act
        await client.StartAsync("127.0.0.1", port);
        string response = await client.SendCommandAsync("qSupported");

        // Assert
        response.Should().NotBeEmpty($"GDB should work with CfgCpu={enableCfgCpu} and InstructionsPerSecond");
    }

    [Fact]
    public async Task GdbClient_Step_ShouldAdvanceOneInstruction() {
        // Arrange
        int port = GetAvailablePort();
        GdbClientProcess client = await StartGdbServerAndConnectAsync(port);

        // Get initial IP
        string initialRegs = await client.SendCommandAsync("g");

        // Act - Single step
        string stepResponse = await client.SendCommandAsync("s");

        // Get new IP
        string newRegs = await client.SendCommandAsync("g");

        // Assert
        stepResponse.Should().NotBeEmpty("Step command should return status");
        // Registers should have changed after stepping
        newRegs.Should().NotBe(initialRegs, "Registers should change after stepping an instruction");
    }

    [Fact]
    public async Task GdbClient_Detach_ShouldDisconnectGracefully() {
        // Arrange
        int port = GetAvailablePort();
        GdbClientProcess client = await StartGdbServerAndConnectAsync(port);

        // Act
        string response = await client.SendCommandAsync("D");

        // Assert
        response.Should().Contain("OK", "Detach should succeed");
    }

    [Fact]
    public async Task GdbClient_MultipleCommands_ShouldWorkInSequence() {
        // Arrange
        int port = GetAvailablePort();
        GdbClientProcess client = await StartGdbServerAndConnectAsync(port);

        // Act - Execute multiple commands
        string supported = await client.SendCommandAsync("qSupported");
        string haltReason = await client.SendCommandAsync("?");
        string registers = await client.SendCommandAsync("g");
        string register0 = await client.SendCommandAsync("p0");

        // Assert
        supported.Should().NotBeEmpty();
        haltReason.Should().NotBeEmpty();
        registers.Should().NotBeEmpty();
        register0.Should().NotBeEmpty();
    }

    private async Task<GdbClientProcess> StartGdbServerAndConnectAsync(int port) {
        await StartGdbServerAsync(port);
        
        GdbClientProcess client = new();
        await client.StartAsync("127.0.0.1", port);
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
        await Task.Delay(1500);

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
