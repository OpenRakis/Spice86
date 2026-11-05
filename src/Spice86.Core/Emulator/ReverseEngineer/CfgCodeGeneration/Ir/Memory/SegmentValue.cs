namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;

/// <summary>
/// What <see cref="IrSegmentValues"/> proves about the value a register holds at a use site: a
/// 16-bit constant, an unknown value shared with every copy of the same SSA root version, or
/// nothing.
/// </summary>
internal abstract record SegmentValue {
    private SegmentValue() {
    }

    /// <summary>A proven 16-bit constant.</summary>
    internal sealed record Known(ushort Value) : SegmentValue;

    /// <summary>
    /// An unknown value whose root is <paramref name="RootVersion"/>: every register version whose
    /// value is a copy chain of that version holds the same value. Two registers are only the same
    /// root when they reach it through an uninterrupted chain of full 16-bit copies; any byte write,
    /// partial write, arithmetic or merge on the way breaks the chain and is
    /// <see cref="Unknown"/> instead.
    /// </summary>
    internal sealed record Symbolic(int RootVersion) : SegmentValue;

    /// <summary>Nothing proven; never equal to anything, not even itself.</summary>
    internal sealed record Unknown : SegmentValue {
        internal static readonly Unknown Instance = new();

        private Unknown() {
        }
    }
}
