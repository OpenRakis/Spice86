namespace Spice86.Http;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Http;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Http.Controllers;
using Spice86.Shared.Interfaces;

using System.Linq;

/// <summary>
/// Built-in Kestrel HTTP API server.
/// </summary>
public sealed class Spice86HttpApiServer : IDisposable {
    private readonly ILoggerService _loggerService;
    private readonly WebApplication _webApplication;
    private bool _disposed;

    /// <summary>Gets the actual TCP port the server is listening on after startup.</summary>
    public int Port { get; private set; }

    /// <summary>Builds and starts the embedded Kestrel HTTP API server.</summary>
    /// <param name="state">CPU register and flag state of the emulator.</param>
    /// <param name="memory">Emulator memory bus.</param>
    /// <param name="pauseHandler">Handler used to query and change the emulator pause state.</param>
    /// <param name="loggerService">Logger service.</param>
    /// <param name="a20Gate">A20 gate used to transform physical addresses before memory access.</param>
    /// <param name="port">TCP port to listen on.</param>
    public Spice86HttpApiServer(State state, IMemory memory,
        IPauseHandler pauseHandler, ILoggerService loggerService, A20Gate a20Gate, int port) {
        _loggerService = loggerService;

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        Serilog.SerilogServiceCollectionExtensions.AddSerilog(
            builder.Services,
            loggerService,
            dispose: false);
        builder.WebHost.UseKestrel();
        builder.WebHost.UseUrls($"http://{HttpApiEndpoint.Host}:{port}");
        builder.Services.AddSingleton<IHostLifetime, EmbeddedHostLifetime>();
        builder.Services
            .AddControllers()
            .AddApplicationPart(typeof(HttpApiController).Assembly);
        builder.Services.AddSingleton(new HttpApiState(state, memory, pauseHandler, a20Gate));

        _webApplication = builder.Build();
        _webApplication.MapControllers();
        _webApplication.Start();

        IServerAddressesFeature? addressesFeature = _webApplication.Services
            .GetService<IServer>()
            ?.Features.Get<IServerAddressesFeature>();
        string? boundAddress = addressesFeature?.Addresses.FirstOrDefault();
        if (boundAddress is not null && Uri.TryCreate(boundAddress, UriKind.Absolute, out Uri? uri)) {
            Port = uri.Port;
        } else {
            Port = port;
        }

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("HTTP API listening on http://{Host}:{Port}", HttpApiEndpoint.Host, Port);
        }
    }

    /// <inheritdoc />
    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        ((IHost)_webApplication).Dispose();
    }

    // custom IHostLifeTime so the HTTP server can close when the emulation loop exits.
    private sealed class EmbeddedHostLifetime : IHostLifetime {
        public Task WaitForStartAsync(CancellationToken cancellationToken) {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) {
            return Task.CompletedTask;
        }
    }
}
