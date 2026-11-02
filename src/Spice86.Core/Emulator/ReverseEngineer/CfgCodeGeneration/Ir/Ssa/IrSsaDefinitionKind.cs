namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

/// <summary>How an SSA version was produced.</summary>
internal enum IrSsaDefinitionKind {
    /// <summary>The value the method is entered with, defined virtually at each entry block.</summary>
    MethodEntry,
    /// <summary>A statement or terminator that fully writes the value.</summary>
    Definition,
    /// <summary>A statement or terminator that may write the value or writes part of it; the new version also depends on the previous one.</summary>
    PartialDefinition,
    /// <summary>A phi at a block entry.</summary>
    Phi
}
