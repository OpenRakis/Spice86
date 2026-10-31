namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ssa;

/// <summary>Why a printed flag read with a single setter cannot trivially inline the setter's computation.</summary>
internal enum FlagPropagationBlocker {
    /// <summary>The flag version is a partial definition: the flag may keep its previous value (a shift by CL).</summary>
    MayWrite,
    /// <summary>The setter's execution AST touches memory (a pointer or the stack).</summary>
    MemoryOperand,
    /// <summary>A register operand the setter reads no longer holds the value it had right after the setter.</summary>
    OperandChanged,
    /// <summary>No blocker: the setter's computation could replace the read.</summary>
    Propagatable
}
