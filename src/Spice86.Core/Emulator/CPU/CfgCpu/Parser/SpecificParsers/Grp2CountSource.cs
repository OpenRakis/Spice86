namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

/// <summary>
/// Identifies the source of the shift/rotate count for GRP2 instructions.
/// </summary>
public enum Grp2CountSource {
    /// <summary>Count is the constant 1.</summary>
    One,
    /// <summary>Count comes from the CL register.</summary>
    Cl,
    /// <summary>Count is an 8-bit immediate read after ModRM.</summary>
    Immediate,
}
