namespace Spice86.Shared.Interfaces; 

/// <summary>
/// Contains properties used internally by the <see cref="ILoggerService"/> to enrich logs. <br/>
/// Each property is updated during execution by the appropriate emulator class.
/// </summary>
public interface ILoggerPropertyBag {
    /// <summary>
    /// From Cpu.State.CS
    /// </summary>
    ushort CodeSegment { get; set; }
    
    /// <summary>
    /// From Cpu.State.IP
    /// </summary>
    ushort InstructionPointer { get; set; }
}