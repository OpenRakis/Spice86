namespace Spice86.Core.Emulator.Mcp;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Serilog;
using Serilog.Core;

using Spice86.Core.Emulator.Http;
using Spice86.Mcp;

using System.Linq;
using System.Reflection;

using ILogger = Microsoft.Extensions.Logging.ILogger;

internal sealed class McpHttpHost : IAsyncDisposable {
    private WebApplication? _app;
    private readonly ILogger _loggerService;
    private Logger? _mcpFileLogger;
    private bool _disposed;

    /// <summary>Gets the actual TCP port the server is listening on after startup.</summary>
    public int Port { get; private set; }

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
        builder.Services.AddSingleton<IHostLifetime, EmbeddedHostLifetime>();

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

        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
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

        try {
            _app.StartAsync().GetAwaiter().GetResult();
        } catch {
            _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _app = null;
            throw;
        }

        IServerAddressesFeature? addressesFeature = _app.Services
            .GetService<IServer>()
            ?.Features.Get<IServerAddressesFeature>();
        string? boundAddress = addressesFeature?.Addresses.FirstOrDefault();
        if (boundAddress is not null && Uri.TryCreate(boundAddress, UriKind.Absolute, out Uri? uri)) {
            Port = uri.Port;
        } else {
            Port = port;
        }

        _loggerService.LogInformation("MCP HTTP server started on http://127.0.0.1:{Port}/mcp", Port);
    }

    public void Stop() {
        Task.Run(() => DisposeAsync().AsTask()).GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync() {
        if (_disposed) {
            return;
        }
        _disposed = true;
        if (_app != null) {
            await _app.StopAsync();
            await _app.DisposeAsync();
            _app = null;
        }
        _mcpFileLogger?.Dispose();
        _mcpFileLogger = null;
        GC.SuppressFinalize(this);
    }
}
