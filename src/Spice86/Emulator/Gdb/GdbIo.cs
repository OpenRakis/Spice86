namespace Spice86.Emulator.Gdb;

using Serilog;

using Spice86.Utils;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

public class GdbIo : IDisposable
{
    private static readonly ILogger _logger = Log.Logger.ForContext<GdbIo>();
    private readonly Socket serverSocket;
    private readonly TcpListener tcpListener;
    private readonly Socket socket;
    private readonly StreamReader input;
    private readonly StreamWriter output;
    private NetworkStream stream;
    private readonly List<byte> rawCommand = new();
    private readonly GdbFormatter gdbFormatter = new();
    private bool disposedValue;

    public GdbIo(int port)
    {
        IPHostEntry host = Dns.GetHostEntry("localhost");
        IPAddress ip = new IPAddress(host.AddressList.First().GetAddressBytes());
        tcpListener = new TcpListener(ip, port);
        serverSocket = tcpListener.Server;
        socket = tcpListener.AcceptSocket();
        _logger.Information("GDB Server listening on port {@Port}", port);
        _logger.Information("Client connected: {@CanonicalHostName}", socket.RemoteEndPoint);
        stream = new NetworkStream(socket);
        input = new StreamReader(stream);
        output = new StreamWriter(stream);
    }

    public List<byte> GetRawCommand()
    {
        return rawCommand;
    }

    public string ReadCommand()
    {
        rawCommand.Clear();
        int chr = input.Read();
        StringBuilder resBuilder = new StringBuilder();
        while (chr >= 0)
        {
            rawCommand.Add((byte)chr);
            if ((char)chr == '#')
            {
                input.Read();
                input.Read();
                break;
            }
            else
            {
                resBuilder.Append((char)chr);
            }

            chr = input.Read();
        }

        return GetPayload(resBuilder);
    }

    private string GetPayload(StringBuilder resBuilder)
    {
        string res = resBuilder.ToString();
        int beginning = res.IndexOf('$');
        if (beginning != -1)
        {
            return res[(beginning + 1)..];
        }

        beginning = res.IndexOf('+');
        if (beginning != -1)
        {
            return res[(beginning + 1)..];
        }

        return res;
    }

    public void SendResponse(string data)
    {
        if (data != null)
        {
            _logger.Information("Sending response {@ResponseData}", data);
            output.Write(Encoding.UTF8.GetBytes(data));
        }
    }

    public string GenerateMessageToDisplayResponse(string message)
    {
        string toSend = message + '\n';
        return this.GenerateResponse(ConvertUtils.ByteArrayToHexString(Encoding.UTF8.GetBytes(toSend)));
    }

    public string GenerateResponse(string data)
    {
        int checksum = 0;
        foreach (byte b in Encoding.UTF8.GetBytes(data))
        {
            checksum += b;
        }

        return "+$" + data + '#' + gdbFormatter.FormatValueAsHex8(checksum);
    }

    public string GenerateUnsupportedResponse()
    {
        return "";
    }

    protected void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                // dispose managed state (managed objects)
                serverSocket.Close();
                socket.Close();
            }
            disposedValue = true;
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}