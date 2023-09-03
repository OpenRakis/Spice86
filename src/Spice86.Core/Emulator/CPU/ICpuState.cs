namespace Spice86.Core.Emulator.CPU;
using Spice86.Shared.Utils;

using System.Text;

/// <summary>
/// Contains all CPU registers and other informations about the state of the CPU
/// </summary>
public interface ICpuState {
    byte AH { get; set; }
    byte AL { get; set; }
    ushort AX { get; set; }
    uint EAX { get; set; }

    // Base
    byte BH { get; set; }
    byte BL { get; set; }
    ushort BX { get; set; }
    uint EBX { get; set; }

    // Counter
    byte CH { get; set; }
    byte CL { get; set; }
    ushort CX { get; set; }
    uint ECX { get; set; }

    // Data
    byte DH { get; set; }
    byte DL { get; set; }
    ushort DX { get; set; }
    uint EDX { get; set; }

    // Destination Index
    ushort DI { get; set; }
    uint EDI { get; set; }

    // Source Index
    ushort SI { get; set; }
    uint ESI { get; set; }

    // Base Pointer
    ushort BP { get; set; }
    uint EBP { get; set; }

    // Stack Pointer
    ushort SP { get; set; }
    uint ESP { get; set; }

    // Code Segment
    ushort CS { get; set; }

    // Data Segment
    ushort DS { get; set; }

    // Extra segments
    ushort ES { get; set; }
    ushort FS { get; set; }
    ushort GS { get; set; }

    // Stack Segment
    ushort SS { get; set; }

    /// <summary> Instruction pointer </summary>
    ushort IP { get; set; }

    /// <summary>
    /// Flags register
    /// </summary>
    Flags Flags { get; }

    bool OverflowFlag { get; set; }
    bool DirectionFlag { get; set; }
    bool InterruptFlag { get; set; }
    bool TrapFlag { get; set; }
    bool SignFlag { get; set; }
    bool ZeroFlag { get; set; }
    bool AuxiliaryFlag { get; set; }
    bool ParityFlag { get; set; }
    bool CarryFlag { get; set; }

    /// <summary>
    /// Gets the offset value of the Direction Flag for 8 bit CPU instructions.
    /// </summary>

    short Direction8 { get; }

    /// <summary>
    /// Gets the offset value of the Direction Flag for 16 bit CPU instructions.
    /// </summary>

    short Direction16 { get; }

    /// <summary>
    /// Gets the offset value of the Direction Flag for 32 bit CPU instructions.
    /// </summary>
    short Direction32 { get; }

    bool? ContinueZeroFlagValue { get; set; }
    int? SegmentOverrideIndex { get; set; }

    /// <summary>
    /// The number of CPU cycles, incremented on each new instruction.
    /// </summary>
    long Cycles { get; }
    uint IpPhysicalAddress { get; }
    uint StackPhysicalAddress { get; }

    Registers Registers { get; }
    SegmentRegisters SegmentRegisters { get; }

    bool IsRunning { get; set; }

    /// <summary>
    /// Sets <see cref="ContinueZeroFlagValue"/> and <see cref="SegmentOverrideIndex"/> to <c>null</c>.
    /// </summary>
    void ClearPrefixes();

    /// <summary>
    /// Increments the <see cref="Cycles"/> count.
    /// </summary>
    void IncCycles();

    /// <summary>
    /// Returns all the CPU registers dumped into a string
    /// </summary>
    /// <returns>All the CPU registers dumped into a string</returns>
    string DumpedRegFlags { get; }

    /// <summary>
    /// Returns all the CPU registers dumped into a string
    /// </summary>
    /// <returns>All the CPU registers dumped into a string</returns>
    string ToString() {
        return DumpedRegFlags;
    }
}
