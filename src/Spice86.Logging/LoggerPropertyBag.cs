namespace Spice86.Logging;

using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <inheritdoc cref="ILoggerPropertyBag"/>
public class LoggerPropertyBag : ILoggerPropertyBag {
    /// <inheritdoc/>
    public SegmentedAddress CsIp { get; set; } = new(0,0);
}