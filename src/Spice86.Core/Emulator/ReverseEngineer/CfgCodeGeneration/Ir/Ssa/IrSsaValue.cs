namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

/// <summary>
/// One value the IR SSA tracks: a whole 32-bit general register (any byte of it is part of the value),
/// a segment register, one of the six status flags, the direction flag, or an opaque resource token
/// (memory contents, IO port state) ordering side effects.
/// </summary>
internal enum IrSsaValue {
    /// <summary>The EAX register.</summary>
    Eax = 0,
    /// <summary>The ECX register.</summary>
    Ecx = 1,
    /// <summary>The EDX register.</summary>
    Edx = 2,
    /// <summary>The EBX register.</summary>
    Ebx = 3,
    /// <summary>The ESP register.</summary>
    Esp = 4,
    /// <summary>The EBP register.</summary>
    Ebp = 5,
    /// <summary>The ESI register.</summary>
    Esi = 6,
    /// <summary>The EDI register.</summary>
    Edi = 7,
    /// <summary>The ES segment register.</summary>
    Es = 8,
    /// <summary>The CS segment register.</summary>
    Cs = 9,
    /// <summary>The SS segment register.</summary>
    Ss = 10,
    /// <summary>The DS segment register.</summary>
    Ds = 11,
    /// <summary>The FS segment register.</summary>
    Fs = 12,
    /// <summary>The GS segment register.</summary>
    Gs = 13,
    /// <summary>The carry flag.</summary>
    Carry = 14,
    /// <summary>The parity flag.</summary>
    Parity = 15,
    /// <summary>The auxiliary flag.</summary>
    Auxiliary = 16,
    /// <summary>The zero flag.</summary>
    Zero = 17,
    /// <summary>The sign flag.</summary>
    Sign = 18,
    /// <summary>The overflow flag.</summary>
    Overflow = 19,
    /// <summary>The direction flag.</summary>
    Direction = 20,
    /// <summary>The memory contents token.</summary>
    Memory = 21,
    /// <summary>The IO port state token.</summary>
    Io = 22,
    /// <summary>How many values exist.</summary>
    Count = 23
}
