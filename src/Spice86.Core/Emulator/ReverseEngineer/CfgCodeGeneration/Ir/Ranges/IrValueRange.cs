namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ranges;

/// <summary>An inclusive unsigned interval; empty when <see cref="Min"/> is over <see cref="Max"/>.</summary>
/// <param name="Min">The smallest value in the range.</param>
/// <param name="Max">The largest value in the range.</param>
internal readonly record struct IrValueRange(ulong Min, ulong Max) {
    /// <summary>The whole domain of <paramref name="bits"/> bits.</summary>
    public static IrValueRange Full(int bits) =>
        new(0, bits >= 64 ? ulong.MaxValue : (1UL << bits) - 1);

    /// <summary>The one-value range.</summary>
    public static IrValueRange Constant(ulong value) => new(value, value);

    /// <summary>Whether no value is in the range.</summary>
    public bool IsEmpty => Min > Max;

    /// <summary>The smallest range containing both.</summary>
    public IrValueRange Union(IrValueRange other) {
        if (IsEmpty) {
            return other;
        }
        if (other.IsEmpty) {
            return this;
        }
        return new IrValueRange(Math.Min(Min, other.Min), Math.Max(Max, other.Max));
    }

    /// <summary>The values in both (empty when disjoint).</summary>
    public IrValueRange Intersect(IrValueRange other) =>
        new(Math.Max(Min, other.Min), Math.Min(Max, other.Max));
}
