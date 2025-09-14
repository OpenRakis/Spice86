﻿namespace Spice86.Core.Emulator.Gdb;

using Serilog.Events;

using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Net;
using System.Net.Sockets;
using System.Text;

/// <summary>
/// Handles the I/O operations for a GDB (GNU Debugger) connection.
/// </summary>
public sealed class GdbIo : IDisposable {
    private readonly ILoggerService _loggerService;
    private readonly GdbFormatter _gdbFormatter = new();
    private readonly List<byte> _rawCommand = new();
    private Socket? _socket;
    private readonly TcpListener _tcpListener;
    private bool _disposed;
    private NetworkStream? _stream;

    /// <summary>
    /// Initializes a new instance of the <see cref="GdbIo"/> class that listens on the specified port for incoming connections.
    /// </summary>
    /// <param name="port">The port number to listen on.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public GdbIo(int port, ILoggerService loggerService) {
        _loggerService = loggerService.WithLogLevel(LogEventLevel.Debug);
        // Listen to connections in IPv4 or IPv6
        _tcpListener = new TcpListener(IPAddress.IPv6Any, port);
        _tcpListener.Server.DualMode = true;
    }

    /// <summary>
    /// Waits for a GDB client to connect to the specified port.
    /// </summary>
    public void WaitForConnection() {
        _tcpListener.Start();
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("GDB Server listening on port {Port}", ((IPEndPoint)_tcpListener.LocalEndpoint).Port);
        }
        try {
            _socket = _tcpListener.AcceptSocket();
            if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                _loggerService.Information("Client connected: {@CanonicalHostName}", _socket.RemoteEndPoint);
            }
            _stream = new NetworkStream(_socket);
        } catch (SocketException e) {
            if (!_disposed) {
                throw new InvalidOperationException("A network error occurred while waiting for client to connect", e);
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the GDB client is still connected to the server.
    /// </summary>
    public bool IsClientConnected() {
        if (_disposed) {
            return false;
        }
        if (_socket is null) {
            return false;
        }
        if(!_socket.Connected) {
            return false;
        }
        
        return !(_socket.Poll(1000, SelectMode.SelectRead) && _socket.Available == 0);
    }

    /// <inheritdoc />
    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Generates a message response to display.
    /// </summary>
    /// <param name="message">The message to include in the response.</param>
    /// <returns>The generated response string.</returns>
    public string GenerateMessageToDisplayResponse(string message) {
        string toSend = $"{message}\n";
        return GenerateResponse(ConvertUtils.ByteArrayToHexString(Encoding.UTF8.GetBytes(toSend)));
    }

    /// <summary>
    /// Generates a response string for a GDB client command.
    /// </summary>
    /// <param name="data">The data to include in the response.</param>
    /// <returns>The generated response string.</returns>
    public string GenerateResponse(string data) {
        byte checksum = 0;
        byte[] array = Encoding.UTF8.GetBytes(data);
        for (int i = 0; i < array.Length; i++) {
            byte b = array[i];
            checksum += b;
        }

        return $"+${data}#{_gdbFormatter.FormatValueAsHex8(checksum)}";
    }

    /// <summary>
    /// Generates an unsupported response string for a GDB client command.
    /// </summary>
    /// <returns>An empty string.</returns>
    public string GenerateUnsupportedResponse() {
        return "";
    }

    /// <summary>
    /// Gets the raw command bytes received from the GDB client.
    /// </summary>
    public List<byte> RawCommand => _rawCommand;

    /// <summary>
    /// Reads a command from the network stream.
    /// </summary>
    /// <returns>A string representing the command.</returns>
    public string ReadCommand() {
        _rawCommand.Clear();
        if (_stream is null) {
            throw new InvalidOperationException("No network stream to read from. Was WaitForConnection called before this?");
        }
        try {
            int chr = _stream.ReadByte();
            StringBuilder resBuilder = new StringBuilder();
            while (chr >= 0) {
                _rawCommand.Add((byte)chr);
                if ((char)chr == '#') {
                    // Ignore checksum
                    _stream.ReadByte();
                    _stream.ReadByte();
                    break;
                }
                resBuilder.Append((char)chr);
                chr = _stream.ReadByte();
            }
            string payload = GetPayload(resBuilder);
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("Received command from GDB {GdbPayload}", payload);
            }
            return payload;
        } catch (SocketException e) {
            if (!_disposed) {
                throw new InvalidOperationException("A network error occurred while reading data", e);
            }
            return "";
        }
    }

    /// <summary>
    /// Sends a response to the connected client.
    /// </summary>
    /// <param name="data">The response data to send.</param>
    public void SendResponse(string? data) {
        if (!IsClientConnected()) {
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("Cannot send response, client is not connected anymore");
            }
            // Happens when the emulator thread reaches a breakpoint but the client is gone
            return;
        }
        if (data != null) {
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("Sending response {ResponseData}", data);
            }
            try {
                _stream?.Write(Encoding.UTF8.GetBytes(data));
            } catch (SocketException e) {
                if (!_disposed) {
                    throw new InvalidOperationException("A network error occurred while writing data", e);
                }
            }
        }
    }

    private void Dispose(bool disposing) {
        if (!_disposed) {
            _disposed = true;
            if (disposing) {
                // dispose managed state (managed objects)
                _stream?.Close();
                _socket?.Shutdown(SocketShutdown.Both);
                _socket?.Close();
                _tcpListener.Stop();
            }
        }
    }

    /// <summary>
    /// Extracts the payload from the given StringBuilder.
    /// </summary>
    /// <param name="resBuilder">The StringBuilder containing the response.</param>
    /// <returns>A string representing the payload.</returns>
    private string GetPayload(StringBuilder resBuilder) {
        string res = resBuilder.ToString();
        int beginning = res.IndexOf('$');
        if (beginning != -1) {
            return res[(beginning + 1)..];
        }

        beginning = res.IndexOf('+');
        if (beginning != -1) {
            return res[(beginning + 1)..];
        }

        return res;
    }
}