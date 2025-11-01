namespace Spice86.Tests.Emulator.Gdb;

using System.Net.Sockets;
using System.Text;

/// <summary>
/// A minimal GDB Remote Serial Protocol (RSP) client for integration testing.
/// Implements the core GDB protocol for communicating with the Spice86 GDB server.
/// </summary>
public class GdbClient : IDisposable {
    private readonly TcpClient _tcpClient;
    private NetworkStream? _stream;
    private bool _disposed;

    /// <summary>
    /// Creates a new GDB client that will connect to the specified host and port.
    /// </summary>
    public GdbClient() {
        _tcpClient = new TcpClient();
    }

    /// <summary>
    /// Connects to the GDB server at the specified host and port.
    /// </summary>
    /// <param name="host">The host to connect to (e.g., "localhost")</param>
    /// <param name="port">The port to connect to</param>
    /// <param name="cancellationToken">Cancellation token for the connection attempt</param>
    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default) {
        await _tcpClient.ConnectAsync(host, port, cancellationToken);
        _tcpClient.NoDelay = true; // Disable Nagle algorithm for immediate sends
        _stream = _tcpClient.GetStream();
    }

    /// <summary>
    /// Sends a raw GDB command and returns the response.
    /// Automatically calculates and appends the checksum.
    /// </summary>
    /// <param name="command">The command to send (without $ prefix or #checksum suffix)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The response payload (without protocol framing)</returns>
    public async Task<string> SendCommandAsync(string command, CancellationToken cancellationToken = default) {
        if (_stream == null) {
            throw new InvalidOperationException("Not connected to GDB server. Call ConnectAsync first.");
        }

        // Calculate checksum
        byte checksum = CalculateChecksum(command);
        
        // Format: $command#checksum
        string packet = $"${command}#{checksum:X2}";
        byte[] data = Encoding.ASCII.GetBytes(packet);
        
        // Send command
        await _stream.WriteAsync(data, 0, data.Length, cancellationToken);
        await _stream.FlushAsync(cancellationToken);

        // Read response with timeout
        using CancellationTokenSource timeoutCts = new(TimeSpan.FromSeconds(5));
        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        
        return await ReadResponseAsync(linkedCts.Token);
    }

    /// <summary>
    /// Reads a response from the GDB server.
    /// </summary>
    private async Task<string> ReadResponseAsync(CancellationToken cancellationToken) {
        if (_stream == null) {
            throw new InvalidOperationException("Not connected to GDB server.");
        }

        StringBuilder response = new();
        bool inPacket = false;
        int checksumCharsRead = 0;
        byte[] buffer = new byte[4096]; // Larger buffer for efficiency
        int bufferPos = 0;
        int bufferLen = 0;

        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            
            // Refill buffer if empty
            if (bufferPos >= bufferLen) {
                bufferLen = await _stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                bufferPos = 0;
                
                if (bufferLen == 0) {
                    throw new IOException("Connection closed while reading response");
                }
            }

            char c = (char)buffer[bufferPos++];

            if (c == '+') {
                // ACK - continue reading actual response
                continue;
            } else if (c == '-') {
                // NACK - should retry, but for testing we'll throw
                throw new IOException("Server sent NACK");
            } else if (c == '$') {
                // Start of packet
                inPacket = true;
                response.Clear();
            } else if (c == '#') {
                // End of packet, checksum follows
                inPacket = false;
                checksumCharsRead = 0;
            } else if (!inPacket && checksumCharsRead < 2) {
                // Reading checksum characters (we ignore them for simplicity)
                checksumCharsRead++;
                if (checksumCharsRead == 2) {
                    // Done reading response
                    return response.ToString();
                }
            } else if (inPacket) {
                response.Append(c);
            }
        }
    }

    /// <summary>
    /// Calculates the GDB protocol checksum for a command.
    /// </summary>
    private static byte CalculateChecksum(string data) {
        byte checksum = 0;
        foreach (char c in data) {
            checksum += (byte)c;
        }
        return checksum;
    }

    /// <summary>
    /// Queries the server for supported features.
    /// </summary>
    public async Task<string> QuerySupportedAsync(CancellationToken cancellationToken = default) {
        return await SendCommandAsync("qSupported", cancellationToken);
    }

    /// <summary>
    /// Queries the halt reason (why execution stopped).
    /// </summary>
    public async Task<string> QueryHaltReasonAsync(CancellationToken cancellationToken = default) {
        return await SendCommandAsync("?", cancellationToken);
    }

    /// <summary>
    /// Reads all CPU registers.
    /// </summary>
    public async Task<string> ReadRegistersAsync(CancellationToken cancellationToken = default) {
        return await SendCommandAsync("g", cancellationToken);
    }

    /// <summary>
    /// Reads a specific register by index.
    /// </summary>
    public async Task<string> ReadRegisterAsync(int registerIndex, CancellationToken cancellationToken = default) {
        return await SendCommandAsync($"p{registerIndex:X}", cancellationToken);
    }

    /// <summary>
    /// Writes a value to a specific register.
    /// </summary>
    public async Task<string> WriteRegisterAsync(int registerIndex, uint value, CancellationToken cancellationToken = default) {
        return await SendCommandAsync($"P{registerIndex:X}={value:X8}", cancellationToken);
    }

    /// <summary>
    /// Reads memory from the specified address.
    /// </summary>
    public async Task<string> ReadMemoryAsync(uint address, int length, CancellationToken cancellationToken = default) {
        return await SendCommandAsync($"m{address:X},{length:X}", cancellationToken);
    }

    /// <summary>
    /// Writes memory to the specified address.
    /// </summary>
    public async Task<string> WriteMemoryAsync(uint address, byte[] data, CancellationToken cancellationToken = default) {
        string hexData = BitConverter.ToString(data).Replace("-", "");
        return await SendCommandAsync($"M{address:X},{data.Length:X}:{hexData}", cancellationToken);
    }

    /// <summary>
    /// Sets a software breakpoint at the specified address.
    /// </summary>
    public async Task<string> SetBreakpointAsync(uint address, CancellationToken cancellationToken = default) {
        return await SendCommandAsync($"Z0,{address:X},1", cancellationToken);
    }

    /// <summary>
    /// Removes a software breakpoint at the specified address.
    /// </summary>
    public async Task<string> RemoveBreakpointAsync(uint address, CancellationToken cancellationToken = default) {
        return await SendCommandAsync($"z0,{address:X},1", cancellationToken);
    }

    /// <summary>
    /// Continues execution.
    /// </summary>
    public async Task<string> ContinueAsync(CancellationToken cancellationToken = default) {
        return await SendCommandAsync("c", cancellationToken);
    }

    /// <summary>
    /// Single-steps one instruction.
    /// </summary>
    public async Task<string> StepAsync(CancellationToken cancellationToken = default) {
        return await SendCommandAsync("s", cancellationToken);
    }

    /// <summary>
    /// Sends a custom monitor command.
    /// </summary>
    public async Task<string> SendMonitorCommandAsync(string command, CancellationToken cancellationToken = default) {
        // Monitor commands are sent via qRcmd with hex-encoded command
        string hexCommand = string.Concat(Encoding.UTF8.GetBytes(command).Select(b => $"{b:X2}"));
        return await SendCommandAsync($"qRcmd,{hexCommand}", cancellationToken);
    }

    /// <summary>
    /// Detaches from the target (disconnects gracefully).
    /// </summary>
    public async Task<string> DetachAsync(CancellationToken cancellationToken = default) {
        return await SendCommandAsync("D", cancellationToken);
    }

    public void Dispose() {
        if (!_disposed) {
            _stream?.Dispose();
            _tcpClient.Dispose();
            _disposed = true;
        }
    }
}
