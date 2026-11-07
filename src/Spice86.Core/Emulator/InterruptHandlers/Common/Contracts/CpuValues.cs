namespace Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// The machine values a statement or a service can read or write. Bits 0 to 37 are the
/// register bytes and segment registers (one bit per byte of the eight 32-bit general
/// registers: register <c>r</c>, byte <c>b</c> = bit <c>4r + b</c>, bytes 0 and 1 the
/// 16-bit register, byte 0 the low 8-bit register, byte 1 the high one; then one bit
/// per segment register at bit <c>32 + index</c>: ES, CS, SS, DS, FS, GS). Bits 38 to
/// 43 are the six status flags; bit 44 is the direction flag. The byte names cover
/// one byte each, the 16-bit names two, the 32-bit names four; the
/// <c>*High</c> names cover the upper word of a 32-bit register. Bit 45 is the FPU control word.
/// </summary>
[System.Flags]
public enum CpuValues : ulong {
    /// <summary>No value.</summary>
    None = 0,
    /// <summary>AL (byte 0 of AX).</summary>
    Al = 1UL << 0,
    /// <summary>AH (byte 1 of AX).</summary>
    Ah = 1UL << 1,
    /// <summary>AX.</summary>
    Ax = 0x3,
    /// <summary>Upper 16 bits of EAX.</summary>
    EaxHigh = 0xC,
    /// <summary>EAX.</summary>
    Eax = 0xF,
    /// <summary>CL (byte 0 of CX).</summary>
    Cl = 1UL << 4,
    /// <summary>CH (byte 1 of CX).</summary>
    Ch = 1UL << 5,
    /// <summary>CX.</summary>
    Cx = 0x3UL << 4,
    /// <summary>Upper 16 bits of ECX.</summary>
    EcxHigh = 0xCUL << 4,
    /// <summary>ECX.</summary>
    Ecx = 0xFUL << 4,
    /// <summary>DL (byte 0 of DX).</summary>
    Dl = 1UL << 8,
    /// <summary>DH (byte 1 of DX).</summary>
    Dh = 1UL << 9,
    /// <summary>DX.</summary>
    Dx = 0x3UL << 8,
    /// <summary>Upper 16 bits of EDX.</summary>
    EdxHigh = 0xCUL << 8,
    /// <summary>EDX.</summary>
    Edx = 0xFUL << 8,
    /// <summary>BL (byte 0 of BX).</summary>
    Bl = 1UL << 12,
    /// <summary>BH (byte 1 of BX).</summary>
    Bh = 1UL << 13,
    /// <summary>BX.</summary>
    Bx = 0x3UL << 12,
    /// <summary>Upper 16 bits of EBX.</summary>
    EbxHigh = 0xCUL << 12,
    /// <summary>EBX.</summary>
    Ebx = 0xFUL << 12,
    /// <summary>SP.</summary>
    Sp = 0x3UL << 16,
    /// <summary>Upper 16 bits of ESP.</summary>
    EspHigh = 0xCUL << 16,
    /// <summary>ESP.</summary>
    Esp = 0xFUL << 16,
    /// <summary>BP.</summary>
    Bp = 0x3UL << 20,
    /// <summary>Upper 16 bits of EBP.</summary>
    EbpHigh = 0xCUL << 20,
    /// <summary>EBP.</summary>
    Ebp = 0xFUL << 20,
    /// <summary>SI.</summary>
    Si = 0x3UL << 24,
    /// <summary>Upper 16 bits of ESI.</summary>
    EsiHigh = 0xCUL << 24,
    /// <summary>ESI.</summary>
    Esi = 0xFUL << 24,
    /// <summary>DI.</summary>
    Di = 0x3UL << 28,
    /// <summary>Upper 16 bits of EDI.</summary>
    EdiHigh = 0xCUL << 28,
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
    /// <summary>FPU control word.</summary>
    FpuControlWord = 1UL << 45,
    /// <summary>Every register byte and segment register.</summary>
    Registers = (1UL << 38) - 1,
    /// <summary>The six status flags.</summary>
    StatusFlags = Carry | Parity | Auxiliary | Zero | Sign | Overflow,
    /// <summary>Every value.</summary>
    All = (1UL << 46) - 1
}
