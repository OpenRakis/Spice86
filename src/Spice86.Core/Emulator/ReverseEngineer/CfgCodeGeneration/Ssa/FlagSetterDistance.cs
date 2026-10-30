namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ssa;

/// <summary>How far the setter of a printed flag read sits from the reading instruction.</summary>
internal enum FlagSetterDistance {
    /// <summary>The reading instruction itself or the node right before it in its block.</summary>
    AdjacentSetter,
    /// <summary>An earlier node of the same block.</summary>
    SameBlockSetter,
    /// <summary>A node of another block.</summary>
    OtherBlockSetter,
    /// <summary>No single setter: a phi, a boundary or the method entry.</summary>
    NoSingleSetter
}
