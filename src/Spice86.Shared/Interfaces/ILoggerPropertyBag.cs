namespace Spice86.Shared.Interfaces;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Contains properties used internally by the <see cref="ILoggerService"/> to enrich logs. <br/>
/// Each property is updated during execution by the appropriate emulator class.
/// </summary>
public interface ILoggerPropertyBag {
    /// <summary>
    /// From Cpu.State.CS and Cpu.State.IP
    /// </summary>
    SegmentedAddress CsIp { get; set; }
}