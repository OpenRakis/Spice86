namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;

/// <summary>A set of the eight general-purpose registers, by their 16-bit name; AL, AH, AX and EAX all count as AX.</summary>
[System.Flags]
internal enum GeneralRegisters : byte {
    None = 0,
    Ax = 1 << 0,
    Cx = 1 << 1,
    Dx = 1 << 2,
    Bx = 1 << 3,
    Sp = 1 << 4,
    Bp = 1 << 5,
    Si = 1 << 6,
    Di = 1 << 7,
    All = 0xFF
}
