namespace Spice86.Core.Emulator.Http;

using Microsoft.Extensions.Hosting;

/// <summary>
/// A no-op host lifetime so embedded web hosts do not hook console signals and stop with the emulator.
/// </summary>
internal sealed class EmbeddedHostLifetime : IHostLifetime {
    public Task WaitForStartAsync(CancellationToken cancellationToken) {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) {
        return Task.CompletedTask;
    }
}
