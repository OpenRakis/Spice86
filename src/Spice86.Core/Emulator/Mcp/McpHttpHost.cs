namespace Spice86.Core.Emulator.Mcp;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Serilog;
using Serilog.Core;

using Spice86.Mcp;

using System.Reflection;
using System.Threading;

using ILogger = Microsoft.Extensions.Logging.ILogger;

internal sealed class McpHttpHost : IAsyncDisposable {
    private static readonly TimeSpan ShutdownJoinTimeout = TimeSpan.FromSeconds(5);
    private WebApplication? _app;
    private Thread? _serverThread;
    private readonly ILogger _loggerService;
    private Logger? _mcpFileLogger;
    private readonly ManualResetEvent _shutdownRequested = new(false);
    private bool _disposed;

    public McpHttpHost(ILogger loggerService) {
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
                // Stateless mode: the server never issues Mcp-Session-Id headers.
                // Stateful sessions cause 404 when AI clients reuse a session ID
                // from a fresh TCP connection or skip the notifications/initialized
                // handshake step, which is a common real-world pattern.
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

        // The MCP SDK does not map GET /mcp in stateless mode (POST only), but
        // some clients (including opencode remote MCP) probe with GET first.
        // Return a minimal SSE endpoint event telling the client to POST here.
        _app.MapGet("/mcp", async (HttpContext context) => {
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache,no-store";
            context.Response.Headers["X-Accel-Buffering"] = "no";
            await context.Response.WriteAsync("event: endpoint\ndata: /mcp/\n\n");
            await context.Response.Body.FlushAsync();
        });

        _serverThread = new Thread(RunServerLoop) {
            Name = "McpHttpHost",
            IsBackground = true
        };
        _serverThread.Start();
        _loggerService.LogInformation("MCP HTTP server started on http://localhost:{Port}/mcp", port);
    }

    private void RunServerLoop() {
        if (_app == null) {
            return;
        }

        try {
            _app.StartAsync().GetAwaiter().GetResult();
            _loggerService.LogInformation("MCP HTTP server is now listening");
        } catch (ObjectDisposedException) {
            // Host disposed while thread was exiting.
            return;
        } catch (Exception ex) {
            _loggerService.LogError(ex, "MCP HTTP server crashed");
            return;
        }

        // Keep this thread alive until the host is asked to stop. Using _app.Run()
        // here caused premature shutdown on background threads because
        // ConsoleLifetime has no console to watch on a non-main thread.
        _shutdownRequested.WaitOne();
    }

    public void Stop() {
        if (_disposed) {
            return;
        }
        _disposed = true;
        _shutdownRequested.Set();
        _app?.Lifetime.StopApplication();
        if (_serverThread is { IsAlive: true }) {
            _serverThread.Join(ShutdownJoinTimeout);
        }
        _app = null;
        _serverThread = null;
        _shutdownRequested.Dispose();
        _mcpFileLogger?.Dispose();
        _mcpFileLogger = null;
    }

    public async ValueTask DisposeAsync() {
        if (_disposed) {
            return;
        }
        _disposed = true;
        _shutdownRequested.Set();
        if (_app != null) {
            _app.Lifetime.StopApplication();
            if (_serverThread is { IsAlive: true }) {
                _serverThread.Join(ShutdownJoinTimeout);
            }
            await _app.DisposeAsync();
            _app = null;
            _serverThread = null;
        }
        _shutdownRequested.Dispose();
        _mcpFileLogger?.Dispose();
        _mcpFileLogger = null;
        GC.SuppressFinalize(this);
    }
}
