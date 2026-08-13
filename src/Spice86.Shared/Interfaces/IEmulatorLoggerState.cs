namespace Spice86.Shared.Interfaces;

using Microsoft.Extensions.Logging;

using Serilog.Core;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Mutable runtime logging state shared between emulator components and Serilog enrichment.
/// </summary>
public interface IEmulatorLoggerState {
    LoggingLevelSwitch LogLevelSwitch { get; set; }
    bool AreLogsSilenced { get; set; }
    SegmentedAddress CsIp { get; set; }
    int ContextIndex { get; set; }
}