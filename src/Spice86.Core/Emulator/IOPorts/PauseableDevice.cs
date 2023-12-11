namespace Spice86.Core.Emulator.IOPorts;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Pause;
using Spice86.Shared.Interfaces;

/// <summary>
/// Represents a device with a render thread that can be paused
/// </summary>
public abstract class PauseableDevice : DefaultIOPortHandler, IPauseable {

    /// <summary>
    /// Initializes a new instance
    /// </summary>
    /// <param name="state">The CPU state</param>
    /// <param name="failOnUnhandledPort">Whether we throw an exception when we don't recognize a port number.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    protected PauseableDevice(State state, bool failOnUnhandledPort, ILoggerService loggerService) : base(state, failOnUnhandledPort, loggerService) {
    }

    /// <inheritdoc/>
    public virtual bool IsPaused { get; set; }

    /// <inheritdoc/>
    public void SleepWhilePaused() {
        while (IsPaused) {
            Thread.Sleep(1);
        }
    }
}
