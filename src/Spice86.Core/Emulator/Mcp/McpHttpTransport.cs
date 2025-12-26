namespace Spice86.Core.Emulator.Mcp;

using Spice86.Shared.Interfaces;

using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

/// <summary>
/// HTTP transport for MCP server supporting SSE (Server-Sent Events).
/// </summary>
public sealed class McpHttpTransport : IDisposable {
    private readonly IMcpServer _mcpServer;
    private readonly ILoggerService _loggerService;
    private readonly int _port;
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<Guid, HttpListenerResponse> _clients = new();

    public McpHttpTransport(IMcpServer mcpServer, ILoggerService loggerService, int port = 8081) {
        _mcpServer = mcpServer;
        _loggerService = loggerService;
        _port = port;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{_port}/");
        _mcpServer.OnNotification += HandleNotification;
    }

    private void HandleNotification(object? sender, string json) {
        _ = BroadcastNotificationAsync(json);
    }

    private async Task BroadcastNotificationAsync(string json) {
        byte[] buffer = Encoding.UTF8.GetBytes($"data: {json}\n\n");
        foreach (var client in _clients) {
            try {
                await client.Value.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                await client.Value.OutputStream.FlushAsync();
            } catch {
                // Client probably disconnected
            }
        }
    }

    public void Start() {
        if (_listener.IsListening) {
            return;
        }

        try {
            _listener.Start();
            _loggerService.Information("MCP HTTP/SSE server started on http://localhost:{Port}/", _port);
            Task.Run(ListenLoop, _cts.Token);
        } catch (Exception ex) {
            _loggerService.Error(ex, "Failed to start MCP HTTP server on port {Port}", _port);
        }
    }

    private async Task ListenLoop() {
        try {
            while (!_cts.Token.IsCancellationRequested && _listener.IsListening) {
                var context = await _listener.GetContextAsync();
                _ = HandleRequestAsync(context);
            }
        } catch (Exception ex) when (ex is ObjectDisposedException or HttpListenerException) {
            // Normal shutdown
        } catch (Exception ex) {
            _loggerService.Error(ex, "Error in MCP HTTP listen loop");
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context) {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;

        try {
            // Enable CORS
            response.AppendHeader("Access-Control-Allow-Origin", "*");
            response.AppendHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.AppendHeader("Access-Control-Allow-Headers", "Content-Type");

            if (request.HttpMethod == "OPTIONS") {
                response.StatusCode = (int)HttpStatusCode.NoContent;
                response.Close();
                return;
            }

            string path = request.Url?.AbsolutePath ?? "";

            if (request.HttpMethod == "GET" && path == "/sse") {
                await HandleSseAsync(context);
            } else if (request.HttpMethod == "POST" && path == "/messages") {
                await HandlePostAsync(context);
            } else {
                response.StatusCode = (int)HttpStatusCode.NotFound;
                response.Close();
            }
        } catch (Exception ex) {
            _loggerService.Error(ex, "Error handling MCP HTTP request {Method} {Path}", request.HttpMethod, request.Url?.AbsolutePath);
            try {
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                response.Close();
            } catch {
                // Ignore errors closing response
            }
        }
    }

    private async Task HandleSseAsync(HttpListenerContext context) {
        HttpListenerResponse response = context.Response;
        response.ContentType = "text/event-stream";
        response.Headers.Add("Cache-Control", "no-cache");
        response.Headers.Add("Connection", "keep-alive");
        
        Guid clientId = Guid.NewGuid();
        _clients.TryAdd(clientId, response);
        _loggerService.Information("New MCP SSE client connected: {ClientId}", clientId);

        try {
            // Send endpoint event
            string endpointMsg = $"event: endpoint\ndata: http://localhost:{_port}/messages\n\n";
            byte[] buffer = Encoding.UTF8.GetBytes(endpointMsg);
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            await response.OutputStream.FlushAsync();

            // Keep connection open
            while (!_cts.Token.IsCancellationRequested) {
                await Task.Delay(15000, _cts.Token);
                // Heartbeat
                await response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(": heartbeat\n\n"));
                await response.OutputStream.FlushAsync();
            }
        } catch (Exception) {
            // Client disconnected
        } finally {
            _clients.TryRemove(clientId, out _);
            _loggerService.Information("MCP SSE client disconnected: {ClientId}", clientId);
            try {
                response.Close();
            } catch {
                // Ignore
            }
        }
    }

    private async Task HandlePostAsync(HttpListenerContext context) {
        using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
        string requestJson = await reader.ReadToEndAsync();
        
        string responseJson;
        try {
            responseJson = _mcpServer.HandleRequest(requestJson);
        } catch (Exception ex) {
            _loggerService.Error(ex, "Error processing MCP request in HTTP transport");
            responseJson = $$"""
            {
              "jsonrpc": "2.0",
              "error": {
                "code": -32603,
                "message": "Internal error: {{ex.Message.Replace("\"", "\\\"")}}"
              },
              "id": null
            }
            """;
        }

        byte[] buffer = Encoding.UTF8.GetBytes(responseJson);
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = buffer.Length;
        await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        await context.Response.OutputStream.FlushAsync();
        context.Response.Close();
    }

    public void Dispose() {
        if (_cts.IsCancellationRequested) {
            return;
        }

        _cts.Cancel();
        _mcpServer.OnNotification -= HandleNotification;
        
        try {
            if (_listener.IsListening) {
                _listener.Stop();
            }
        } catch (ObjectDisposedException) {
            // Already disposed
        } catch (Exception ex) {
            _loggerService.Error(ex, "Error stopping MCP HTTP listener");
        }

        try {
            _listener.Close();
        } catch (ObjectDisposedException) {
            // Already disposed
        }

        _cts.Dispose();

        foreach (var client in _clients.Values) {
            try {
                client.Close();
            } catch {
                // Ignore
            }
        }
    }
}
