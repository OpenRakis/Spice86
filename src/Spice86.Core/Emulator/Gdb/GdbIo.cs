namespace Spice86.Core.Emulator.Gdb;

using Serilog;

using Spice86.Core.Utils;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

public sealed class GdbIo : IDisposable {
    private readonly ILogger _logger;
    private readonly GdbFormatter _gdbFormatter = new();
    private readonly List<byte> _rawCommand = new();
    private Socket? _socket;
    private readonly TcpListener _tcpListener;
    private bool _disposed;
    private NetworkStream? _stream;

    public GdbIo(int port, ILogger logger) {
        _logger = logger;
        IPHostEntry host = Dns.GetHostEntry("localhost");
        IPAddress ip = new IPAddress(host.AddressList.First().GetAddressBytes());
        _tcpListener = new TcpListener(ip, port);
    }

    public void WaitForConnection() {
        _tcpListener.Start();
        _socket = _tcpListener.AcceptSocket();
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            int port = ((IPEndPoint)_tcpListener.LocalEndpoint).Port;
            _logger.Information("GDB Server listening on port {@Port}", port);
            _logger.Information("Client connected: {@CanonicalHostName}", _socket.RemoteEndPoint);
        }
        _stream = new NetworkStream(_socket);
    }

    public bool IsClientConnected => !(_socket is null || !_socket.Connected || (_socket.Poll(1000, SelectMode.SelectRead) && _socket.Available == 0));

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public string GenerateMessageToDisplayResponse(string message) {
        string toSend = $"{message}\n";
        return GenerateResponse(ConvertUtils.ByteArrayToHexString(Encoding.UTF8.GetBytes(toSend)));
    }

    public string GenerateResponse(string data) {
        byte checksum = 0;
        byte[] array = Encoding.UTF8.GetBytes(data);
        for (int i = 0; i < array.Length; i++) {
            byte b = array[i];
            checksum += b;
        }

        return $"+${data}#{_gdbFormatter.FormatValueAsHex8(checksum)}";
    }

    public string GenerateUnsupportedResponse() {
        return "";
    }

    public List<byte> RawCommand => _rawCommand;

    public string ReadCommand() {
        _rawCommand.Clear();
        if (_stream is null) {
            throw new InvalidOperationException("No network stream to read from. Was WaitForConnection called before this?");
        }
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
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("Received command from GDB {@GDBPayload}", payload);
        }
        return payload;
    }

    public void SendResponse(string? data) {
        if (!IsClientConnected) {
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                _logger.Information("Cannot send response, client is not connected anymore.");
            }
            // Happens when the emulator thread reaches a breakpoint but the client is gone
            return;
        }
        if (data != null) {
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                _logger.Information("Sending response {@ResponseData}", data);
            }
            _stream?.Write(Encoding.UTF8.GetBytes(data));
        }
    }

    private void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                // dispose managed state (managed objects)
                _tcpListener.Stop();
                _socket?.Close();
                _stream?.Close();
            }
            _disposed = true;
        }
    }

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