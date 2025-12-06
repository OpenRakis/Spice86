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

        builder.WebHost.ConfigureKestrel(kestrel => {
            kestrel.ListenLocalhost(port);
        });

        _app = builder.Build();
        _app.MapGet("/health", () => Results.Json(new {
            status = "ok",
            service = "Spice86 MCP Server"
        }));
        _app.MapMcp("/mcp");

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
            _app.Run();
        } catch (ObjectDisposedException) {
            // Host disposed while thread was exiting.
        } catch (InvalidOperationException ex) {
            _loggerService.Error(ex, "MCP HTTP server stopped unexpectedly");
        }
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
