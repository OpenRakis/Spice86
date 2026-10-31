namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

/// <summary>
/// The opaque resource versions a statement can read or write: memory contents and IO port state.
/// A token write is a partial definition: it always depends on the previous token version.
/// <see cref="Io"/> accesses are always both read and written, so port accesses stay in program order.
/// </summary>
[System.Flags]
internal enum IrTokens {
    /// <summary>No token.</summary>
    None = 0,
    /// <summary>Memory contents.</summary>
    Memory = 1,
    /// <summary>IO port state.</summary>
    Io = 2,
    /// <summary>Every token.</summary>
    All = Memory | Io
}
