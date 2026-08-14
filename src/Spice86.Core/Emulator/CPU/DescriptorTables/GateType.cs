namespace Spice86.Core.Emulator.CPU.DescriptorTables;

/// <summary>
/// The type of an IDT/GDT gate descriptor or a GDT/LDT system descriptor, decoded from the low 4
/// bits of its type byte. Values match the Intel-defined type-field encodings.
/// </summary>
public enum GateType {
    /// <summary>16-bit call gate.</summary>
    CallGate16 = 0x4,

    /// <summary>Task gate (shared encoding between the 286 and 386).</summary>
    TaskGate = 0x5,

    /// <summary>16-bit interrupt gate.</summary>
    InterruptGate16 = 0x6,

    /// <summary>16-bit trap gate.</summary>
    TrapGate16 = 0x7,

    /// <summary>32-bit call gate.</summary>
    CallGate32 = 0xC,

    /// <summary>32-bit interrupt gate: clears the interrupt flag on entry.</summary>
    InterruptGate32 = 0xE,

    /// <summary>32-bit trap gate: leaves the interrupt flag unchanged on entry.</summary>
    TrapGate32 = 0xF
}
