namespace Spice86.Logging;

using Serilog.Core;

using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// Mutable runtime logging state shared by Spice86 logger consumers.
/// </summary>
public sealed class Spice86LoggerState : IEmulatorLoggerState {
    /// <summary>
    /// Gets the dynamic minimum log level switch.
    /// </summary>
    public LoggingLevelSwitch LogLevelSwitch { get; set; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether all log output is suppressed.
    /// </summary>
    public bool AreLogsSilenced { get; set; }

    /// <summary>
    /// Gets or sets the current instruction address to enrich log events with.
    /// </summary>
    public SegmentedAddress CsIp { get; set; } = new(0, 0);

    /// <summary>
    /// Gets or sets the current execution-context depth.
    /// </summary>
    public int ContextIndex { get; set; }
}