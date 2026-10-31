namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ssa;

/// <summary>What produced the flag version a printed flag read sees.</summary>
internal enum FlagSetterKind {
    /// <summary>An instruction whose execution AST contains an ALU call.</summary>
    AluSetter,
    /// <summary>An instruction that assigns the flag register (<c>popf</c>, <c>sahf</c>) without an ALU call.</summary>
    FlagRegisterSetter,
    /// <summary>Any other defining instruction (<c>clc</c>, <c>stc</c>, plain expansions, ...).</summary>
    PlainSetter,
    /// <summary>A phi at a block entry.</summary>
    JoinSetter,
    /// <summary>A boundary version (a call, interrupt, callback or faulting instruction before the read).</summary>
    BoundarySetter,
    /// <summary>The method entry version: nothing in the method set the flag.</summary>
    EntrySetter
}
