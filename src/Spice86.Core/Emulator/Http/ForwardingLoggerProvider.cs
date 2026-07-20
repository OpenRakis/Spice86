namespace Spice86.Core.Emulator.Http;

using Microsoft.Extensions.Logging;

using Spice86.Shared.Interfaces;

/// <summary>
/// An <see cref="ILoggerProvider"/> that forwards all log messages to a shared <see cref="ILoggerService"/>.
/// </summary>
internal sealed class ForwardingLoggerProvider(ILoggerService target) : ILoggerProvider {
    public ILogger CreateLogger(string categoryName) => target;

    public void Dispose() {
        // The target is owned externally; do not dispose.
    }
}
