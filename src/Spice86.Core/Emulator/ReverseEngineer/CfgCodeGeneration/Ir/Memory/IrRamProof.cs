namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;

/// <summary>What the alias analysis proves about one access being ordinary RAM.</summary>
internal enum IrRamProof {
    /// <summary>Every reachable byte is ordinary RAM: below the device mappings.</summary>
    Proved,
    /// <summary>A contrary safety proof holds: every reachable byte is a device range.</summary>
    ProvedUnsafe,
    /// <summary>The analysis does not have enough knowledge to decide either way.</summary>
    Unknown
}

/// <summary>Whether an access may be treated as ordinary RAM under the generation policy, with
/// the provenance of the permission: proved by the analysis, or assumed by an enabled
/// relaxation.</summary>
/// <param name="Proof">What the analysis proved.</param>
/// <param name="Assumed">Whether an enabled relaxation filled the missing knowledge:
/// <see cref="IrRamProof.Unknown"/> under
/// <see cref="GenerationPolicy.AssumeUnknownMemoryIsRam"/>.</param>
internal sealed record IrRamDecision(IrRamProof Proof, bool Assumed) {
    /// <summary>Whether the access may be treated as ordinary RAM: proved, or assumed by the
    /// policy. A <see cref="IrRamProof.ProvedUnsafe"/> access is never allowed.</summary>
    public bool Allowed => Proof == IrRamProof.Proved || Assumed;
}
