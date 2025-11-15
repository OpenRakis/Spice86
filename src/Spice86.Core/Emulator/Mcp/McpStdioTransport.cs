namespace Spice86.Core.Emulator.Mcp;

using Spice86.Shared.Interfaces;

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Stdio transport layer for MCP (Model Context Protocol) server.
/// Implements the standard MCP transport protocol: reading JSON-RPC requests from stdin and writing responses to stdout.
/// This transport enables external tools and AI models to communicate with the emulator via standard I/O streams.
/// </summary>
public sealed class McpStdioTransport : IDisposable {
    private readonly IMcpServer _mcpServer;
    private readonly ILoggerService _loggerService;
    private readonly TextReader _inputReader;
    private readonly TextWriter _outputWriter;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private Task? _readerTask;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpStdioTransport"/> class.
    /// </summary>
    /// <param name="mcpServer">The MCP server to handle requests.</param>
    /// <param name="loggerService">The logger service for diagnostics.</param>
    public McpStdioTransport(IMcpServer mcpServer, ILoggerService loggerService)
        : this(mcpServer, loggerService, Console.In, Console.Out) {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="McpStdioTransport"/> class with custom I/O streams.
    /// Used primarily for testing.
    /// </summary>
    /// <param name="mcpServer">The MCP server to handle requests.</param>
    /// <param name="loggerService">The logger service for diagnostics.</param>
    /// <param name="inputReader">The input stream to read from.</param>
    /// <param name="outputWriter">The output stream to write to.</param>
    internal McpStdioTransport(IMcpServer mcpServer, ILoggerService loggerService, TextReader inputReader, TextWriter outputWriter) {
        _mcpServer = mcpServer;
        _loggerService = loggerService;
        _inputReader = inputReader;
        _outputWriter = outputWriter;
        _cancellationTokenSource = new CancellationTokenSource();
    }

    /// <summary>
    /// Starts the stdio transport, reading requests from stdin and writing responses to stdout.
    /// This method runs in a background task and continues until stopped or an error occurs.
    /// </summary>
    public void Start() {
        if (_readerTask != null) {
            throw new InvalidOperationException("MCP stdio transport is already started");
        }

        _loggerService.Information("MCP server starting with stdio transport");
        _readerTask = Task.Run(async () => await RunAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
    }

    /// <summary>
    /// Stops the stdio transport gracefully.
    /// </summary>
    public void Stop() {
        if (_readerTask == null) {
            return;
        }

        _loggerService.Information("MCP server stopping");
        _cancellationTokenSource.Cancel();

        try {
            _readerTask.Wait(TimeSpan.FromSeconds(5));
        } catch (AggregateException ex) when (ex.InnerException is OperationCanceledException) {
            // Expected when canceling
        } catch (Exception ex) {
            _loggerService.Error(ex, "Error stopping MCP stdio transport");
        }

        _readerTask = null;
    }

    private async Task RunAsync(CancellationToken cancellationToken) {
        StringBuilder messageBuffer = new StringBuilder();

        try {
            while (!cancellationToken.IsCancellationRequested) {
                string? line = await ReadLineAsync(_inputReader, cancellationToken);

                if (line == null) {
                    // End of stream
                    _loggerService.Information("MCP server: stdin closed, shutting down");
                    break;
                }

                // MCP uses newline-delimited JSON-RPC messages
                if (string.IsNullOrWhiteSpace(line)) {
                    continue;
                }

                messageBuffer.Append(line);

                // Process the complete JSON-RPC message
                string requestJson = messageBuffer.ToString();
                messageBuffer.Clear();

                try {
                    string responseJson = _mcpServer.HandleRequest(requestJson);

                    // Write response to stdout with newline delimiter
                    await WriteLineAsync(_outputWriter, responseJson, cancellationToken);
                    await _outputWriter.FlushAsync();
                } catch (Exception ex) {
                    _loggerService.Error(ex, "Error processing MCP request: {Request}", requestJson);

                    // Send error response
                    string errorResponse = CreateErrorResponse($"Internal error: {ex.Message}");
                    await WriteLineAsync(_outputWriter, errorResponse, cancellationToken);
                    await _outputWriter.FlushAsync();
                }
            }
        } catch (OperationCanceledException) {
            // Normal shutdown
            _loggerService.Information("MCP server shutdown requested");
        } catch (Exception ex) {
            _loggerService.Error(ex, "Fatal error in MCP stdio transport");
        }
    }

    private static async Task<string?> ReadLineAsync(TextReader reader, CancellationToken cancellationToken) {
        // TextReader.ReadLineAsync doesn't support cancellation tokens in .NET Standard 2.0
        // For .NET 10, we can use the version with cancellation token
        return await reader.ReadLineAsync().WaitAsync(cancellationToken);
    }

    private static async Task WriteLineAsync(TextWriter writer, string text, CancellationToken cancellationToken) {
        await writer.WriteLineAsync(text.AsMemory(), cancellationToken);
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

    /// <inheritdoc />
    public void Dispose() {
        if (_disposed) {
            return;
        }

        Stop();
        _cancellationTokenSource.Dispose();
        _disposed = true;
    }
}
