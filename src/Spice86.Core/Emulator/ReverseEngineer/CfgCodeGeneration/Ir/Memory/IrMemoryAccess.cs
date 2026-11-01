namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

/// <summary>
/// What one memory access of a statement touches, at the level the alias analysis can prove.
/// The segment part is a segment register plus the version the statement reads, or a constant;
/// <see cref="SegmentValue"/> is the known 16-bit segment value when the analysis proves one
/// (constant definition, or a phi of equal constants). The offset is a sorted list of
/// (register, version) terms plus a 16-bit constant displacement, or unknown; offsets wrap at
/// <c>0xFFFF</c>. <see cref="LinearAddress"/> is the constant linear address of an
/// <see cref="AbsolutePointerNode"/>.
/// </summary>
internal sealed record IrMemoryAccess {
    /// <summary>The segment register the access goes through, or null when it is a constant or
    /// unknown value - and for an absolute pointer.</summary>
    public IrSsaValue? SegmentRegister { get; init; }

    /// <summary>The version of <see cref="SegmentRegister"/> the access reads, -1 without one.</summary>
    public int SegmentVersion { get; init; } = -1;

    /// <summary>The known 16-bit segment value, when the analysis proves one.</summary>
    public ushort? SegmentValue { get; init; }

    /// <summary>The (register, version) terms of the offset, sorted; <c>null</c> when the offset is
    /// unknown.</summary>
    public IReadOnlyList<OffsetTerm>? OffsetTerms { get; init; }

    /// <summary>The constant displacement of the offset, a signed 16-bit value.</summary>
    public int OffsetDisplacement { get; init; }

    /// <summary>The access width in bytes.</summary>
    public int Width { get; init; }

    /// <summary>The constant linear address of an absolute pointer, or unknown.</summary>
    public uint? LinearAddress { get; init; }
}
