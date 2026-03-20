namespace Spice86.Mcp;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks;

using Spice86.Core.Emulator.Mcp;
using Spice86.Shared.Interfaces;

/// <summary>
/// Hosts an embedded Kestrel web server that exposes the MCP Streamable HTTP transport.
/// Runs alongside the Avalonia UI on a separate port.
/// </summary>
public sealed class McpHttpHost : IDisposable, IAsyncDisposable {
    private WebApplication? _app;
    private readonly ILoggerService _loggerService;
    private bool _disposed;
    private readonly ConcurrentDictionary<Guid, Channel<string>> _legacyClients = new();
    private IMcpServer? _legacyMcpServer;

    public McpHttpHost(ILoggerService loggerService) {
        _loggerService = loggerService;
    }

    public void Start(EmulatorMcpServices services, int port = 8081,
        IEnumerable<Assembly>? additionalToolAssemblies = null,
        IEnumerable<object>? additionalServices = null,
        bool enableLegacyMcp = false,
        IMcpServer? legacyMcpServer = null) {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();

        // Suppress all ASP.NET Core logging to avoid polluting emulator console output
        builder.Logging.ClearProviders();

        builder.Services.AddSingleton(services);

        // Register additional services (from override projects) before building
        if (additionalServices != null) {
            foreach (object service in additionalServices) {
                builder.Services.AddSingleton(service.GetType(), service);
            }
        }

        if (!enableLegacyMcp) {
            IMcpServerBuilder mcpBuilder = builder.Services
                .AddMcpServer(options => {
                    options.ServerInfo = new() {
                        Name = "Spice86 MCP Server",
                        Version = "2.0.0"
                    };
                })
                .WithHttpTransport()
                .WithToolsFromAssembly(typeof(EmulatorMcpTools).Assembly);

            if (additionalToolAssemblies != null) {
                foreach (Assembly assembly in additionalToolAssemblies) {
                    mcpBuilder.WithToolsFromAssembly(assembly);
                }
            }
        }

        builder.WebHost.ConfigureKestrel(kestrel => {
            kestrel.ListenLocalhost(port);
        });

        _app = builder.Build();

        if (enableLegacyMcp && legacyMcpServer != null) {
            ConfigureLegacyEndpoints(_app, port, legacyMcpServer);
            _loggerService.Information("Legacy MCP HTTP routes enabled on /sse and /messages");
        } else if (enableLegacyMcp) {
            _loggerService.Warning("Legacy MCP HTTP routes requested but IMcpServer was not provided. Skipping legacy endpoints.");
        } else {
            _app.MapMcp("/mcp");
        }

        Task runTask = _app.RunAsync();
        _ = runTask.ContinueWith(task => {
            _loggerService.Error(task.Exception, "MCP HTTP server stopped unexpectedly");
        }, TaskContinuationOptions.OnlyOnFaulted);
        _loggerService.Information("MCP HTTP server started on http://localhost:{Port}/mcp", port);
    }

    private void ConfigureLegacyEndpoints(WebApplication app, int port, IMcpServer legacyMcpServer) {
        _legacyMcpServer = legacyMcpServer;
        _legacyMcpServer.OnNotification += HandleLegacyNotification;

        app.MapMethods("/sse", ["OPTIONS"], context => {
            AppendCorsHeaders(context.Response, port);
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });

        app.MapMethods("/messages", ["OPTIONS"], context => {
            AppendCorsHeaders(context.Response, port);
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });

        app.MapGet("/sse", async context => {
            AppendCorsHeaders(context.Response, port);
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";

            Guid clientId = Guid.NewGuid();
            Channel<string> channel = Channel.CreateUnbounded<string>();
            _legacyClients[clientId] = channel;
            _loggerService.Information("New MCP SSE client connected: {ClientId}", clientId);

            try {
                await context.Response.WriteAsync($"event: endpoint\ndata: http://localhost:{port}/messages\n\n", context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);

                while (!context.RequestAborted.IsCancellationRequested) {
                    Task<bool> waitForMessage = channel.Reader.WaitToReadAsync(context.RequestAborted).AsTask();
                    Task heartbeatDelay = Task.Delay(TimeSpan.FromSeconds(15), context.RequestAborted);
                    Task completed = await Task.WhenAny(waitForMessage, heartbeatDelay);

                    if (completed == waitForMessage) {
                        bool channelHasData = await waitForMessage;
                        if (!channelHasData) {
                            // Channel writer was completed (server shutting down) — stop the SSE loop.
                            break;
                        }
                        while (channel.Reader.TryRead(out string? message)) {
                            await context.Response.WriteAsync($"data: {message}\n\n", context.RequestAborted);
                            await context.Response.Body.FlushAsync(context.RequestAborted);
                        }
                    } else {
                        await context.Response.WriteAsync(": heartbeat\n\n", context.RequestAborted);
                        await context.Response.Body.FlushAsync(context.RequestAborted);
                    }
                }
            } catch (OperationCanceledException) {
                // Client disconnected.
            } catch (IOException ex) {
                _loggerService.Warning(ex, "MCP SSE client disconnected abruptly: {ClientId}", clientId);
            } catch (InvalidOperationException ex) {
                _loggerService.Warning(ex, "MCP SSE client disconnected abruptly: {ClientId}", clientId);
            } finally {
                _legacyClients.TryRemove(clientId, out _);
                _loggerService.Information("MCP SSE client disconnected: {ClientId}", clientId);
            }
        });

        app.MapPost("/messages", async context => {
            AppendCorsHeaders(context.Response, port);
            using StreamReader reader = new(context.Request.Body);
            string requestJson = await reader.ReadToEndAsync();

            string? responseJson;
            try {
                responseJson = legacyMcpServer.HandleRequest(requestJson);
            } catch (JsonException ex) {
                _loggerService.Error(ex, "JSON error processing MCP request in legacy HTTP transport");
                responseJson = $$"""
                {
                  "jsonrpc": "2.0",
                  "error": {
                    "code": -32603,
                    "message": "Internal error: {{EscapeJsonString(ex.Message)}}"
                  },
                  "id": null
                }
                """;
            } catch (InvalidOperationException ex) {
                _loggerService.Error(ex, "Error processing MCP request in legacy HTTP transport");
                responseJson = $$"""
                {
                  "jsonrpc": "2.0",
                  "error": {
                    "code": -32603,
                    "message": "Internal error: {{EscapeJsonString(ex.Message)}}"
                  },
                  "id": null
                }
                """;
            }

            if (responseJson == null) {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(responseJson);
        });
    }

    private static void AppendCorsHeaders(HttpResponse response, int port) {
        response.Headers["Access-Control-Allow-Origin"] = $"http://localhost:{port}";
        response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
    }

    private static string EscapeJsonString(string value) {
        return value
            .Replace("\\", @"\\")
            .Replace("\"", "\\\"");
    }

    private void HandleLegacyNotification(object? sender, string json) {
        foreach (Channel<string> client in _legacyClients.Values) {
            client.Writer.TryWrite(json);
        }
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }
        _disposed = true;
        DisposeSynchronousResources();
        // Kestrel (_app) intentionally left running: stopping it requires async operations
        // that cannot be performed safely in a synchronous Dispose() without sync-over-async.
        // Callers that need graceful Kestrel shutdown must use DisposeAsync() instead.
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync() {
        if (_disposed) {
            return;
        }
        _disposed = true;

        DisposeSynchronousResources();

        if (_app != null) {
            try {
                await _app.StopAsync();
                await _app.DisposeAsync();
            } catch (OperationCanceledException ex) {
                _loggerService.Warning(ex, "MCP HTTP host shutdown was cancelled");
            } catch (ObjectDisposedException ex) {
                _loggerService.Warning(ex, "MCP HTTP host was already disposed during shutdown");
            }
            _app = null;
        }
    }

    private void DisposeSynchronousResources() {
        if (_legacyMcpServer != null) {
            _legacyMcpServer.OnNotification -= HandleLegacyNotification;
            _legacyMcpServer = null;
        }

        foreach (Channel<string> client in _legacyClients.Values) {
            client.Writer.TryComplete();
        }
        _legacyClients.Clear();
    }
}
