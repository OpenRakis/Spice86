namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;

/// <summary>
/// SP at a point of the method, expressed as the statement that last gave it a fresh base and the
/// constant delta SP moved since: <c>SP = base + Delta</c>. A null <see cref="Base"/> is the
/// method entry. <see cref="IrStackOffsetKind.Known"/> offsets compare by reference on
/// <see cref="Base"/>, so two points known equal share the same base statement.
/// </summary>
internal readonly record struct IrStackOffset(IrStackOffsetKind Kind, IrStatement? Base, int Delta) {
    /// <summary>No path reaches this point.</summary>
    public static IrStackOffset Unreached => new(IrStackOffsetKind.Unreached, null, 0);

    /// <summary>Some reaching path left the SP difference unknown.</summary>
    public static IrStackOffset Unknown => new(IrStackOffsetKind.Unknown, null, 0);

    /// <summary>SP equals the SP of the method entry.</summary>
    public static IrStackOffset Entry => new(IrStackOffsetKind.Known, null, 0);

    /// <summary>SP sits <paramref name="delta"/> bytes away from where <paramref name="base"/>
    /// left it.</summary>
    public static IrStackOffset Known(IrStatement? @base, int delta) =>
        new(IrStackOffsetKind.Known, @base, delta);

    /// <summary>The join of two offsets: the common value when both agree, unreached when only
    /// one side is reached, unknown otherwise.</summary>
    public static IrStackOffset Join(IrStackOffset first, IrStackOffset second) {
        if (first == second) {
            return first;
        }
        return (first.Kind, second.Kind) switch {
            (IrStackOffsetKind.Unreached, _) => second,
            (_, IrStackOffsetKind.Unreached) => first,
            _ => Unknown
        };
    }

    public bool Equals(IrStackOffset other) =>
        Kind == other.Kind && Delta == other.Delta && ReferenceEquals(Base, other.Base);

    public override int GetHashCode() => HashCode.Combine((int)Kind, Delta);
}
