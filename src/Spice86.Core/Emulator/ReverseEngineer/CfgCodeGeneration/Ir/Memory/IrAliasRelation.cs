namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;

/// <summary>How two memory accesses relate through their addresses.</summary>
internal enum IrAliasRelation {
    /// <summary>The addresses cannot be told apart: they may, but need not, intersect.</summary>
    MayAlias,
    /// <summary>The byte ranges cannot intersect.</summary>
    Disjoint,
    /// <summary>The byte ranges are the same.</summary>
    Equal
}
