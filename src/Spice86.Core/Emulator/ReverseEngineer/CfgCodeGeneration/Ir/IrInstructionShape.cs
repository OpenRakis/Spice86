namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

/// <summary>The instruction shape a lifter tags on an <see cref="IrMethod"/> so later passes read the tag
/// instead of re-detecting the shape from the AST.</summary>
internal enum IrInstructionShape {
    /// <summary>No special shape.</summary>
    None,
    /// <summary>The lifter emitted a <c>DivisionNode</c> for the instruction.</summary>
    Division,
    /// <summary>A <c>CMPS</c> or <c>SCAS</c> instruction with a <c>REPE</c> or <c>REPNE</c> prefix.</summary>
    RepCompare,
    /// <summary>An <c>XCHG</c> instruction.</summary>
    Exchange,
    /// <summary>A far-pointer load: <c>LES</c>, <c>LDS</c>, <c>LSS</c>, <c>LFS</c> or <c>LGS</c>.</summary>
    LoadFarPointer
}
