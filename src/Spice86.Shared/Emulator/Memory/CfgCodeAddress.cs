namespace Spice86.Shared.Emulator.Memory;

/// <summary>
/// A CFG code address: either a real/V86-mode <see cref="Memory.SegmentedAddress"/> (selector:offset,
/// <c>Linear = segment*16+offset</c>) or a protected-mode flat linear address. Exists so protected-mode C#
/// override registration can be keyed by the address code actually executes at, which survives a
/// descriptor edit that repoints a selector's base - unlike <see cref="Memory.SegmentedAddress"/>, whose
/// <see cref="Memory.SegmentedAddress.Linear"/> is always <c>segment*16+offset</c> and has no notion of a
/// GDT/LDT-resolved base. Equality and ordering are defined purely by <see cref="Linear"/>: two instances
/// referring to the same linear address are equal regardless of whether one was constructed from a
/// <see cref="Memory.SegmentedAddress"/> and the other from a raw linear value.
/// </summary>
public readonly record struct CfgCodeAddress : IComparable<CfgCodeAddress> {
    private readonly SegmentedAddress? _segmentedAddress;

    /// <summary>
    /// Constructs a real/V86-mode address from a segment:offset pair.
    /// </summary>
    public CfgCodeAddress(SegmentedAddress segmentedAddress) {
        _segmentedAddress = segmentedAddress;
        Linear = segmentedAddress.Linear;
    }

    /// <summary>
    /// Constructs a protected-mode flat linear address with no associated segment:offset pair.
    /// </summary>
    public CfgCodeAddress(uint linearAddress) {
        _segmentedAddress = null;
        Linear = linearAddress;
    }

    /// <summary>
    /// The segment:offset pair this address was constructed from, or <c>null</c> if it was constructed
    /// from a raw linear address.
    /// </summary>
    public SegmentedAddress? SegmentedAddress => _segmentedAddress;

    /// <summary>
    /// The flat linear address, used for equality, ordering, and hashing.
    /// </summary>
    public uint Linear { get; }

    /// <inheritdoc/>
    public bool Equals(CfgCodeAddress other) => Linear == other.Linear;

    /// <inheritdoc/>
    public override int GetHashCode() => Linear.GetHashCode();

    /// <inheritdoc/>
    public int CompareTo(CfgCodeAddress other) => Linear.CompareTo(other.Linear);

    /// <summary>
    /// Implicitly wraps a <see cref="Memory.SegmentedAddress"/> as a <see cref="CfgCodeAddress"/>.
    /// </summary>
    public static implicit operator CfgCodeAddress(SegmentedAddress address) => new(address);

    /// <inheritdoc/>
    public override string ToString() => _segmentedAddress?.ToString() ?? $"0x{Linear:X8}";
}
