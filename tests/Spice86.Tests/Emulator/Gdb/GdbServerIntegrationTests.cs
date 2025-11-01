namespace Spice86.Tests.Emulator.Gdb;

using FluentAssertions;

using Spice86.Core.CLI;
using Spice86.Core.Emulator;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound;

using System.Net.Sockets;
using System.Text;

using Xunit;

using static Spice86.Core.Emulator.CPU.CpuModel;

/// <summary>
/// Integration tests for GDB server that actually start the server and communicate over TCP.
/// Tests both headless mode and InstructionsPerSecond mode which is forced when GDB is enabled.
/// </summary>
public class GdbServerIntegrationTests : IDisposable {
    private readonly List<Spice86DependencyInjection> _injections = new();
    private readonly List<TcpClient> _clients = new();

    public void Dispose() {
        foreach (TcpClient client in _clients) {
            try {
                client.Close();
                client.Dispose();
            } catch {
                // Ignore cleanup errors
            }
        }

        foreach (Spice86DependencyInjection injection in _injections) {
            try {
                injection.Dispose();
            } catch {
                // Ignore cleanup errors
            }
        }
    }

    [Fact]
    public async Task GdbServer_ShouldAcceptConnection_AndRespondToQueryCommands() {
        // Arrange
        int port = GetAvailablePort();
        Spice86DependencyInjection injection = CreateSpice86WithGdb(port);
        _injections.Add(injection);

        // Wait for server to start
        await Task.Delay(500);

        TcpClient client = new();
        _clients.Add(client);

        // Act
        await client.ConnectAsync("127.0.0.1", port);
        NetworkStream stream = client.GetStream();

        // Send a query command: qSupported
        string command = "$qSupported#37";
        byte[] commandBytes = Encoding.ASCII.GetBytes(command);
        await stream.WriteAsync(commandBytes);
        await stream.FlushAsync();

        // Read response
        byte[] buffer = new byte[1024];
        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
        string response = Encoding.ASCII.GetString(buffer, 0, bytesRead);

        // Assert
        response.Should().StartWith("+");
        response.Should().Contain("$");
        response.Should().Contain("#");
    }

    [Theory]
    [InlineData(false)] // Without CfgCpu
    [InlineData(true)]  // With CfgCpu
    public async Task GdbServer_WithInstructionsPerSecond_ShouldWorkWithBothCpuModes(bool enableCfgCpu) {
        // Arrange
        int port = GetAvailablePort();
        Spice86DependencyInjection injection = CreateSpice86WithGdb(port, enableCfgCpu, instructionsPerSecond: 100000);
        _injections.Add(injection);

        // Wait for server to start
        await Task.Delay(500);

        TcpClient client = new();
        _clients.Add(client);

        // Act
        await client.ConnectAsync("127.0.0.1", port);
        NetworkStream stream = client.GetStream();

        // Send query command to verify server is responsive
        string command = "$qSupported#37";
        byte[] commandBytes = Encoding.ASCII.GetBytes(command);
        await stream.WriteAsync(commandBytes);
        await stream.FlushAsync();

        // Read response
        byte[] buffer = new byte[1024];
        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
        string response = Encoding.ASCII.GetString(buffer, 0, bytesRead);

        // Assert
        response.Should().NotBeEmpty();
        response.Should().StartWith("+");
    }

    [Fact]
    public async Task GdbServer_ReadRegisters_ShouldReturnCpuState() {
        // Arrange
        int port = GetAvailablePort();
        Spice86DependencyInjection injection = CreateSpice86WithGdb(port);
        _injections.Add(injection);

        // Set some register values
        injection.Machine.Cpu.State.AX = 0x1234;
        injection.Machine.Cpu.State.BX = 0x5678;

        await Task.Delay(500);

        TcpClient client = new();
        _clients.Add(client);
        await client.ConnectAsync("127.0.0.1", port);
        NetworkStream stream = client.GetStream();

        // Act - Send read all registers command
        string command = "$g#67";
        byte[] commandBytes = Encoding.ASCII.GetBytes(command);
        await stream.WriteAsync(commandBytes);
        await stream.FlushAsync();

        // Read response
        byte[] buffer = new byte[2048];
        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
        string response = Encoding.ASCII.GetString(buffer, 0, bytesRead);

        // Assert
        response.Should().NotBeEmpty();
        response.Should().StartWith("+$");
        // Response should contain register values
        // AX=0x1234 should appear as 34120000 in the response (little-endian 32-bit)
        response.Should().Contain("34120000");
        // BX=0x5678 should appear as 78560000
        response.Should().Contain("78560000");
    }

    [Fact]
    public async Task GdbServer_CustomMonitorCommands_ShouldWork() {
        // Arrange
        int port = GetAvailablePort();
        Spice86DependencyInjection injection = CreateSpice86WithGdb(port);
        _injections.Add(injection);

        await Task.Delay(500);

        TcpClient client = new();
        _clients.Add(client);
        await client.ConnectAsync("127.0.0.1", port);
        NetworkStream stream = client.GetStream();

        // Act - Send monitor help command
        // Format: qRcmd,{hex-encoded-command}
        // "help" in hex is 68656C70
        string command = "$qRcmd,68656C70#";
        // Calculate checksum
        string commandWithoutDelimiters = "qRcmd,68656C70";
        byte checksum = CalculateChecksum(commandWithoutDelimiters);
        command = $"$qRcmd,68656C70#{checksum:X2}";

        byte[] commandBytes = Encoding.ASCII.GetBytes(command);
        await stream.WriteAsync(commandBytes);
        await stream.FlushAsync();

        // Read response
        byte[] buffer = new byte[4096];
        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
        string response = Encoding.ASCII.GetString(buffer, 0, bytesRead);

        // Assert
        response.Should().NotBeEmpty();
        response.Should().StartWith("+$");
    }

    [Fact]
    public async Task GdbServer_InHeadlessMode_ShouldAcceptConnection() {
        // Arrange
        int port = GetAvailablePort();
        Configuration config = new() {
            Exe = "Resources/cpuTests/add.bin",
            ExpectedChecksumValue = Array.Empty<byte>(),
            GdbPort = port,
            HeadlessMode = HeadlessType.Minimal,
            InstructionsPerSecond = 100000,
            AudioEngine = AudioEngine.Dummy,
            CfgCpu = false,
            InitializeDOS = false
        };

        Spice86DependencyInjection injection = new(config);
        injection.Machine.CpuState.Flags.CpuModel = ZET_86;
        _injections.Add(injection);

        await Task.Delay(500);

        TcpClient client = new();
        _clients.Add(client);

        // Act
        await client.ConnectAsync("127.0.0.1", port);
        NetworkStream stream = client.GetStream();

        // Send a basic query
        string command = "$?#3F";
        byte[] commandBytes = Encoding.ASCII.GetBytes(command);
        await stream.WriteAsync(commandBytes);
        await stream.FlushAsync();

        // Read response
        byte[] buffer = new byte[1024];
        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
        string response = Encoding.ASCII.GetString(buffer, 0, bytesRead);

        // Assert
        response.Should().NotBeEmpty();
        client.Connected.Should().BeTrue();
    }

    private static Spice86DependencyInjection CreateSpice86WithGdb(int port, bool enableCfgCpu = false, long? instructionsPerSecond = null) {
        // Create a minimal test binary path
        string testBinPath = "Resources/cpuTests/add.bin";
        
        // Create configuration with GDB enabled
        Configuration config = new() {
            Exe = testBinPath,
            ExpectedChecksumValue = Array.Empty<byte>(),
            GdbPort = port,
            InstructionsPerSecond = instructionsPerSecond ?? 100000,
            HeadlessMode = HeadlessType.Minimal,
            CfgCpu = enableCfgCpu,
            AudioEngine = AudioEngine.Dummy,
            InitializeDOS = false
        };

        Spice86DependencyInjection injection = new(config);
        injection.Machine.CpuState.Flags.CpuModel = ZET_86;
        return injection;
    }

    private static int GetAvailablePort() {
        TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static byte CalculateChecksum(string data) {
        byte checksum = 0;
        foreach (char c in data) {
            checksum += (byte)c;
        }
        return checksum;
    }
}
