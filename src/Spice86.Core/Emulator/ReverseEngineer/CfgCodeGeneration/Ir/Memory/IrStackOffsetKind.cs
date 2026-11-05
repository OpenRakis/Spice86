namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;

/// <summary>
/// Whether the difference between the current SP and the entry SP is known at a point of the
/// method: <see cref="Known"/> carries the statement that last gave SP a fresh base and the
/// constant delta since.
/// </summary>
internal enum IrStackOffsetKind {
    /// <summary>No path reaches this point.</summary>
    Unreached,

    /// <summary>SP minus the statement's base SP is a known constant.</summary>
    Known,

    /// <summary>Some reaching path left the SP difference unknown.</summary>
    Unknown
}
