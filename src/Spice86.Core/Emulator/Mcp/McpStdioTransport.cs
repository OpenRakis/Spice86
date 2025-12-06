namespace Spice86.Core.Emulator.Mcp;

using Spice86.Shared.Interfaces;

using System;
using System.IO;
using System.Text;
using System.Threading;

/// <summary>
/// Stdio transport for MCP server using synchronous I/O.
/// </summary>
public sealed class McpStdioTransport : IDisposable {
    private readonly IMcpServer _mcpServer;
    private readonly ILoggerService _loggerService;
    private readonly TextReader _inputReader;
    private readonly TextWriter _outputWriter;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private Thread? _readerThread;
    private bool _disposed;

    public McpStdioTransport(IMcpServer mcpServer, ILoggerService loggerService)
        : this(mcpServer, loggerService, Console.In, Console.Out) {
    }

    internal McpStdioTransport(IMcpServer mcpServer, ILoggerService loggerService, TextReader inputReader, TextWriter outputWriter) {
        _mcpServer = mcpServer;
        _loggerService = loggerService;
        _inputReader = inputReader;
        _outputWriter = outputWriter;
        _cancellationTokenSource = new CancellationTokenSource();
    }

    public void Start() {
        if (_readerThread != null) {
            throw new InvalidOperationException("MCP stdio transport is already started");
        }

        _loggerService.Information("MCP server starting with stdio transport");
        _readerThread = new Thread(Run) { IsBackground = true };
        _readerThread.Start();
    }

    public void Stop() {
        if (_readerThread == null) {
            return;
        }

        _loggerService.Information("MCP server stopping");
        _cancellationTokenSource.Cancel();

        if (!_readerThread.Join(TimeSpan.FromSeconds(5))) {
            _loggerService.Warning("MCP server thread did not stop within timeout");
        }

        _readerThread = null;
    }

    private void Run() {
        StringBuilder messageBuffer = new StringBuilder();

        try {
            while (!_cancellationTokenSource.Token.IsCancellationRequested) {
                string? line = _inputReader.ReadLine();

                if (line == null) {
                    _loggerService.Information("MCP server: stdin closed, shutting down");
                    break;
                }

                if (string.IsNullOrWhiteSpace(line)) {
                    continue;
                }

                messageBuffer.Append(line);

                string requestJson = messageBuffer.ToString();
                messageBuffer.Clear();

                try {
                    string responseJson = _mcpServer.HandleRequest(requestJson);

                    _outputWriter.WriteLine(responseJson);
                    _outputWriter.Flush();
                } catch (InvalidOperationException ex) {
                    _loggerService.Error(ex, "Error processing MCP request: {Request}", requestJson);

                    string errorResponse = CreateErrorResponse($"Internal error: {ex.Message}");
                    _outputWriter.WriteLine(errorResponse);
                    _outputWriter.Flush();
                } catch (ArgumentException ex) {
                    _loggerService.Error(ex, "Invalid MCP request: {Request}", requestJson);

                    string errorResponse = CreateErrorResponse($"Invalid request: {ex.Message}");
                    _outputWriter.WriteLine(errorResponse);
                    _outputWriter.Flush();
                } catch (IOException ex) {
                    _loggerService.Error(ex, "I/O error processing MCP request: {Request}", requestJson);

                    string errorResponse = CreateErrorResponse($"I/O error: {ex.Message}");
                    _outputWriter.WriteLine(errorResponse);
                    _outputWriter.Flush();
                }
            }
        } catch (OperationCanceledException) {
            _loggerService.Information("MCP server shutdown requested");
        } catch (IOException ex) {
            _loggerService.Error(ex, "Fatal I/O error in MCP stdio transport");
        } catch (ObjectDisposedException ex) {
            _loggerService.Error(ex, "Stream disposed in MCP stdio transport");
        }
    }

    private static string CreateErrorResponse(string message) {
        return $$"""
        {
          "jsonrpc": "2.0",
          "error": {
            "code": -32603,
            "message": "{{message.Replace("\"", "\\\"")}}"
          },
          "id": null
        }
        """;
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }

        Stop();
        _cancellationTokenSource.Dispose();
        _disposed = true;
    }
}