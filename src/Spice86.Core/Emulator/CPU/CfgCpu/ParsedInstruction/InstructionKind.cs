namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

/// <summary>
/// Flags that classify an instruction's control-flow role.
/// </summary>
[Flags]
public enum InstructionKind {
    /// <summary>Normal, non-control-flow instruction.</summary>
    None = 0,

    /// <summary>Instruction is a CALL (near or far, direct or indirect).</summary>
    Call = 1 << 0,

    /// <summary>Instruction is a JMP (conditional or unconditional, near or far).</summary>
    Jump = 1 << 1,

    /// <summary>Instruction is a RET or IRET.</summary>
    Return = 1 << 2,

    /// <summary>Instruction encodes an invalid opcode.</summary>
    Invalid = 1 << 3,
}
