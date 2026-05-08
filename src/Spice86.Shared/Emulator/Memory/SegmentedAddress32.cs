namespace Spice86.Shared.Emulator.Memory;

/// <summary>
/// An address represented with a real-mode segment and a 32-bit offset.
/// Used for x86 32-bit far pointers (segment:offset32, 6 bytes: 4-byte EIP + 2-byte CS).
/// </summary>
public readonly record struct SegmentedAddress32 {
    /// <summary>
    /// Initializes a new instance of the <see cref="SegmentedAddress32"/> struct.
    /// </summary>
    /// <param name="segment">The segment value of the address.</param>
    /// <param name="offset">The 32-bit offset value of the address.</param>
    public SegmentedAddress32(ushort segment, uint offset) {
        Segment = segment;
        Offset = offset;
    }

    /// <summary>
    /// Gets the segment value of the address.
    /// </summary>
    public ushort Segment { get; }

    /// <summary>
    /// Gets the 32-bit offset value of the address.
    /// </summary>
    public uint Offset { get; }

    /// <summary>
    /// Converts this 32-bit segmented address to a 16-bit <see cref="SegmentedAddress"/> by truncating the offset.
    /// Only lossless when the offset fits in 16 bits (0x0000–0xFFFF).
    /// </summary>
    public SegmentedAddress ToSegmentedAddress() {
        return new SegmentedAddress(Segment, (ushort)Offset);
    }

    /// <inheritdoc/>
    public override string ToString() {
        return $"{Segment:X4}:{Offset:X8}";
    }
}
