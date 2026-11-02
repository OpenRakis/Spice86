namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;

/// <summary>A set of the six status flags; each value is the flag's bit in the FLAGS register.</summary>
[System.Flags]
internal enum StatusFlags : ushort {
    None = 0,
    Carry = Flags.Carry,
    Parity = Flags.Parity,
    Auxiliary = Flags.Auxiliary,
    Zero = Flags.Zero,
    Sign = Flags.Sign,
    Overflow = Flags.Overflow,
    All = Carry | Parity | Auxiliary | Zero | Sign | Overflow
}
