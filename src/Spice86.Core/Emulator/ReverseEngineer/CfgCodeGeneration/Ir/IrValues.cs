namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

/// <summary>
/// The machine values a statement can read or write. Bits 0 to 37 are the register bytes and
/// segment registers (one bit per byte of the eight 32-bit general registers: register <c>r</c>,
/// byte <c>b</c> = bit <c>4r + b</c>, bytes 0 and 1 the 16-bit register, byte 0 the low 8-bit
/// register, byte 1 the high one; then one bit per segment register at bit <c>32 + index</c>:
/// ES, CS, SS, DS, FS, GS). Bits 38 to 43 are the six status flags; bit 44 is the direction flag.
/// The 16-bit names cover two bytes each, the 32-bit names four.
/// </summary>
[System.Flags]
internal enum IrValues : ulong {
    /// <summary>No value.</summary>
    None = 0,
    /// <summary>AX.</summary>
    Ax = 0x3,
    /// <summary>CX.</summary>
    Cx = 0x3UL << 4,
    /// <summary>DX.</summary>
    Dx = 0x3UL << 8,
    /// <summary>BX.</summary>
    Bx = 0x3UL << 12,
    /// <summary>SP.</summary>
    Sp = 0x3UL << 16,
    /// <summary>BP.</summary>
    Bp = 0x3UL << 20,
    /// <summary>SI.</summary>
    Si = 0x3UL << 24,
    /// <summary>DI.</summary>
    Di = 0x3UL << 28,
    /// <summary>EAX.</summary>
    Eax = 0xF,
    /// <summary>ECX.</summary>
    Ecx = 0xFUL << 4,
    /// <summary>EDX.</summary>
    Edx = 0xFUL << 8,
    /// <summary>EBX.</summary>
    Ebx = 0xFUL << 12,
    /// <summary>ESP.</summary>
    Esp = 0xFUL << 16,
    /// <summary>EBP.</summary>
    Ebp = 0xFUL << 20,
    /// <summary>ESI.</summary>
    Esi = 0xFUL << 24,
    /// <summary>EDI.</summary>
    Edi = 0xFUL << 28,
    /// <summary>ES.</summary>
    Es = 1UL << 32,
    /// <summary>CS.</summary>
    Cs = 1UL << 33,
    /// <summary>SS.</summary>
    Ss = 1UL << 34,
    /// <summary>DS.</summary>
    Ds = 1UL << 35,
    /// <summary>FS.</summary>
    Fs = 1UL << 36,
    /// <summary>GS.</summary>
    Gs = 1UL << 37,
    /// <summary>Carry flag.</summary>
    Carry = 1UL << 38,
    /// <summary>Parity flag.</summary>
    Parity = 1UL << 39,
    /// <summary>Auxiliary flag.</summary>
    Auxiliary = 1UL << 40,
    /// <summary>Zero flag.</summary>
    Zero = 1UL << 41,
    /// <summary>Sign flag.</summary>
    Sign = 1UL << 42,
    /// <summary>Overflow flag.</summary>
    Overflow = 1UL << 43,
    /// <summary>Direction flag.</summary>
    Direction = 1UL << 44,
    /// <summary>Every register byte and segment register.</summary>
    Registers = (1UL << 38) - 1,
    /// <summary>The six status flags.</summary>
    StatusFlags = Carry | Parity | Auxiliary | Zero | Sign | Overflow,
    /// <summary>Every value.</summary>
    All = (1UL << 45) - 1
}
