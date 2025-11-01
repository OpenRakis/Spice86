namespace Spice86.Tests.Emulator.Gdb;

using FluentAssertions;

using Spice86.Core.CLI;
using Spice86.Core.Emulator;
using Spice86.Core.Emulator.Devices.Sound;

using System.Net.Sockets;
using System.Text;

using Xunit;

using static Spice86.Core.Emulator.CPU.CpuModel;

/// <summary>
/// Simple integration test to verify GDB server basic connectivity and protocol.
/// </summary>
[Collection("GDB Integration Tests")]
public class GdbBasicConnectivityTests : IDisposable {
    private readonly List<Spice86DependencyInjection> _injections = new();
    private readonly List<TcpClient> _clients = new();
    private readonly List<Task> _executionTasks = new();

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
                injection.Machine.CpuState.IsRunning = false;
                injection.Dispose();
            } catch {
                // Ignore cleanup errors
            }
        }

        // Don't wait for execution tasks - they'll be terminated by IsRunning = false
    }

    [Fact]
    public async Task GdbServer_ShouldAcceptTcpConnection() {
        // Arrange
        int port = GetAvailablePort();
        await StartGdbServerAsync(port);

        // Act
        TcpClient client = new();
        _clients.Add(client);
        await client.ConnectAsync("127.0.0.1", port);

        // Assert
        client.Connected.Should().BeTrue("GDB server should accept TCP connections");
    }

    [Fact(Skip = "Test uses raw TCP - use GdbFullIntegrationTests with separate process instead")]
    public async Task GdbServer_ShouldReceiveRawCommand() {
        // Arrange
        int port = GetAvailablePort();
        await StartGdbServerAsync(port);

        TcpClient client = new();
        _clients.Add(client);
        client.NoDelay = true;
        await client.ConnectAsync("127.0.0.1", port);
        NetworkStream stream = client.GetStream();

        // Act - Send a raw GDB command (? = query halt reason)
        byte checksum = (byte)'?';
        string command = $"$?#{checksum:X2}";
        byte[] data = Encoding.ASCII.GetBytes(command);
        await stream.WriteAsync(data, 0, data.Length);
        await stream.FlushAsync();

        // Give server time to process
        await Task.Delay(100);

        // Try to read response
        byte[] buffer = new byte[1024];
        bool hasData = stream.DataAvailable;

        // Assert
        hasData.Should().BeTrue("Server should send a response");
    }

    private async Task StartGdbServerAsync(int port) {
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
    }

    private static int GetAvailablePort() {
        using TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
