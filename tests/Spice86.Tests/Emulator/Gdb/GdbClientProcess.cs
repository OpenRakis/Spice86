namespace Spice86.Tests.Emulator.Gdb;

using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

/// <summary>
/// Manages a separate .NET process that acts as a GDB client, communicating via named pipes.
/// This approach avoids threading issues and mimics how a real GDB client would interact with the server.
/// </summary>
public class GdbClientProcess : IDisposable {
    private Process? _process;
    private NamedPipeServerStream? _pipeServer;
    private readonly string _pipeName;
    private bool _disposed;

    public GdbClientProcess() {
        // Use short pipe name to avoid Unix domain socket path length limit (104 chars)
        // The full path includes temp directory, so we need to keep the name very short
        _pipeName = $"gdb{Guid.NewGuid():N}".Substring(0, 16);
    }

    /// <summary>
    /// Starts the GDB client process that will connect to the specified GDB server.
    /// </summary>
    /// <param name="gdbHost">GDB server host</param>
    /// <param name="gdbPort">GDB server port</param>
    public async Task StartAsync(string gdbHost, int gdbPort) {
        // Create named pipe server for communication with the client process
        _pipeServer = System.OperatingSystem.IsWindows() 
            ? new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous)
            : new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        // Start the GDB client process
        string dllPath = typeof(GdbClientProcess).Assembly.Location;
        
        ProcessStartInfo startInfo = new() {
            FileName = "dotnet",
            Arguments = $"exec \"{dllPath}\" --gdb-client {gdbHost} {gdbPort} {_pipeName}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        _process = Process.Start(startInfo);
        if (_process == null) {
            throw new InvalidOperationException("Failed to start GDB client process");
        }

        // Wait for the client process to connect to our pipe
        await _pipeServer.WaitForConnectionAsync();
    }

    /// <summary>
    /// Sends a command to the GDB client process and waits for the response.
    /// </summary>
    /// <param name="command">The GDB command to send (without protocol framing)</param>
    /// <returns>The response from the GDB server</returns>
    public async Task<string> SendCommandAsync(string command) {
        if (_pipeServer == null || !_pipeServer.IsConnected) {
            throw new InvalidOperationException("Client process not started or not connected");
        }

        // Send command to client process
        byte[] commandBytes = Encoding.UTF8.GetBytes(command);
        byte[] lengthPrefix = BitConverter.GetBytes(commandBytes.Length);
        
        await _pipeServer.WriteAsync(lengthPrefix, 0, lengthPrefix.Length);
        await _pipeServer.WriteAsync(commandBytes, 0, commandBytes.Length);
        await _pipeServer.FlushAsync();

        // Read response from client process
        byte[] lengthBuffer = new byte[4];
        int bytesRead = await _pipeServer.ReadAsync(lengthBuffer, 0, 4);
        if (bytesRead != 4) {
            throw new IOException("Failed to read response length");
        }

        int responseLength = BitConverter.ToInt32(lengthBuffer, 0);
        byte[] responseBytes = new byte[responseLength];
        
        int totalRead = 0;
        while (totalRead < responseLength) {
            bytesRead = await _pipeServer.ReadAsync(responseBytes, totalRead, responseLength - totalRead);
            if (bytesRead == 0) {
                throw new IOException("Connection closed while reading response");
            }
            totalRead += bytesRead;
        }

        return Encoding.UTF8.GetString(responseBytes);
    }

    public void Dispose() {
        if (!_disposed) {
            _pipeServer?.Dispose();
            
            if (_process != null && !_process.HasExited) {
                _process.Kill();
                _process.WaitForExit(1000);
            }
            _process?.Dispose();
            _disposed = true;
        }
    }
}
