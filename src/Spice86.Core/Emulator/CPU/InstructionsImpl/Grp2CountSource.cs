namespace Spice86.Core.Emulator.CPU.InstructionsImpl;

using Spice86.Core.Emulator.CPU;

/// <summary>
/// Enum representing the source of the count for Group 2 instructions in the CPU.
/// </summary>
public enum Grp2CountSource
{
    /// <summary>
    /// Represents a count source of one.
    /// </summary>
    One,

    /// <summary>
    /// Represents a count source of the CL register.
    /// </summary>
    CL,

    /// <summary>
    /// Represents a count source of the next unsigned 8-bit integer from memory pointed by <see cref="Cpu.InternalIpPhysicalAddress"/>.
    /// </summary>
    NextUint8
}
