namespace Spice86.Core.Emulator.Mcp;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Serilog;
using Serilog.Core;

using Spice86.Mcp;
using Spice86.Shared.Interfaces;

using System.Reflection;
using System.Threading;

internal sealed class McpHttpHost : IDisposable {
    private static readonly TimeSpan ShutdownJoinTimeout = TimeSpan.FromMilliseconds(25);
    private WebApplication? _app;
    private Thread? _serverThread;
    private readonly ILoggerService _loggerService;
    private Logger? _mcpFileLogger;
    private bool _disposed;

    public McpHttpHost(ILoggerService loggerService) {
        _loggerService = loggerService;
    }

    public void Start(EmulatorMcpServices services, int port = 8081,
        IEnumerable<Assembly>? additionalToolAssemblies = null,
        IEnumerable<object>? additionalServices = null) {
        _mcpFileLogger = new LoggerConfiguration()
            .MinimumLevel.Warning()
            .WriteTo.File("logs/mcp.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(_mcpFileLogger);

        builder.Services.AddSingleton(services);

        // Register additional services (from override projects) before building
        if (additionalServices != null) {
            foreach (object service in additionalServices) {
                builder.Services.AddSingleton(service.GetType(), service);
            }
        }

        IMcpServerBuilder mcpBuilder = builder.Services
            .AddMcpServer(options => {
                options.ServerInfo = new() {
                    Name = "Spice86 MCP Server",
                    Version = "2.0.0"
                };
            })
            .WithHttpTransport(options => {
                // Stateless mode keeps things simple — no session management,
                // no Mcp-Session-Id headers, compatible with AI clients.
                // The SDK does not map GET /mcp in stateless mode (POST only),
                // but we add our own GET handler below to satisfy clients
                // (like opencode remote MCP) that probe with GET first.
                options.Stateless = true;
            })
            .WithToolsFromAssembly(typeof(EmulatorMcpTools).Assembly);

        if (additionalToolAssemblies != null) {
            foreach (Assembly assembly in additionalToolAssemblies) {
                mcpBuilder.WithToolsFromAssembly(assembly);
            }
        }

        builder.WebHost.UseUrls($"http://localhost:{port}");
        _app = builder.Build();
        _app.MapGet("/health", () => Results.Json(new {
            status = "ok",
            service = "Spice86 MCP Server"
        }));
        _app.MapMcp("/mcp");

        // The SDK doesn't map GET /mcp in stateless mode, but some MCP clients
        // (including opencode remote MCP) probe with GET first. Return a minimal
        // SSE endpoint event telling the client to POST to the same URL.
        _app.MapGet("/mcp", async (HttpContext context) =>
        {
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache,no-store";
            context.Response.Headers["X-Accel-Buffering"] = "no";
            await context.Response.WriteAsync(
                $"event: endpoint\ndata: /mcp/\n\n");
            await context.Response.Body.FlushAsync();
        });

        _serverThread = new Thread(RunServerLoop) {
            Name = "McpHttpHost",
            IsBackground = true
        };
        _serverThread.Start();
        _loggerService.Information("MCP HTTP server started on http://localhost:{Port}/mcp", port);
    }

    private void RunServerLoop() {
        if (_app == null) {
            return;
        }

        try {
            _app.StartAsync().GetAwaiter().GetResult();
            _loggerService.Information("MCP HTTP server is now listening");
            // Block this thread indefinitely so the server keeps running.
            // Using _app.Run() instead caused premature shutdown on background
            // threads because ConsoleLifetime has no console to watch on a
            // non-main thread.
        } catch (ObjectDisposedException) {
            // Host disposed while thread was exiting.
        } catch (Exception ex) {
            _loggerService.Error(ex, "MCP HTTP server crashed");
        }

        // Wait forever so the thread never exits.
        new System.Threading.ManualResetEvent(false).WaitOne();
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }
        _disposed = true;

        if (_app != null) {
            _app.Lifetime.StopApplication();
            if (_serverThread is { IsAlive: true }) {
                _serverThread.Join(ShutdownJoinTimeout);
            }
            _serverThread = null;
        }
        _mcpFileLogger?.Dispose();
        _mcpFileLogger = null;
        GC.SuppressFinalize(this);
    }
}
