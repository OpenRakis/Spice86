namespace Spice86.Logging;

using Spice86.Shared;
using Spice86.Shared.Interfaces;

/// <inheritdoc/>
public class LoggerPropertyBag : ILoggerPropertyBag {
    public SegmentedAddress CsIp { get; set; } = new(0,0);
}