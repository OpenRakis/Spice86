namespace Spice86.Core.Emulator.InterruptHandlers;

using Spice86.Shared.Interfaces;

/// <summary>
/// Receives the shared logger-state instance used for interrupt log enrichment.
/// </summary>
public interface IInterruptLoggerStateConsumer {
    void SetLoggerState(IEmulatorLoggerState loggerState);
}