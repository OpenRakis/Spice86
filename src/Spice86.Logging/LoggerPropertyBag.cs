namespace Spice86.Logging;

using Spice86.Shared.Interfaces;

/// <inheritdoc/>
public class LoggerPropertyBag : ILoggerPropertyBag {
    public ushort CodeSegment { get; set; }
    public ushort InstructionPointer { get; set; }
}