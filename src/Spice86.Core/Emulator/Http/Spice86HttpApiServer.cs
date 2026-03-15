namespace Spice86.Core.Emulator.Http;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Http.Controllers;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
/// Built-in Kestrel HTTP API server.
/// </summary>
public sealed class Spice86HttpApiServer : IDisposable {
    private readonly ILoggerService _loggerService;
    private readonly WebApplication _webApplication;
    private bool _disposed;

    public Spice86HttpApiServer(State state, IMemory memory,
        IPauseHandler pauseHandler, ILoggerService loggerService) {
        _loggerService = loggerService;

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel();
        builder.WebHost.UseUrls(HttpApiEndpoint.BaseUrl);
        builder.Services.AddSingleton<IHostLifetime, EmbeddedHostLifetime>();
        builder.Services
            .AddControllers()
            .AddApplicationPart(typeof(HttpApiController).Assembly);
        builder.Services.AddSingleton(new HttpApiState(state, memory, pauseHandler));

        _webApplication = builder.Build();
        _webApplication.MapControllers();
        _webApplication.Start();

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("HTTP API listening on {BaseUrl}", HttpApiEndpoint.BaseUrl);
        }
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        DisposeWebApp();
    }

    private void DisposeWebApp() {
        ExecuteShutdownAction(
            () => ((IHost)_webApplication).Dispose(),
            "dispose");
    }

    private void ExecuteShutdownAction(Action action, string operation) {
        try {
            action();
        } catch (ObjectDisposedException exception) {
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug(exception, "HTTP API server was already disposed while attempting to {Operation}.", operation);
            }
        }
    }

    private sealed class EmbeddedHostLifetime : IHostLifetime {
        public Task WaitForStartAsync(CancellationToken cancellationToken) {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) {
            return Task.CompletedTask;
        }
    }
}
