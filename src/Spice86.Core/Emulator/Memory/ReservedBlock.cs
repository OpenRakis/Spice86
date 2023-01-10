namespace Spice86.Core.Emulator.Memory;

/// <summary>
/// Describes a reserved block of conventional memory.
/// </summary>
public sealed class ReservedBlock : IEquatable<ReservedBlock> {
    internal ReservedBlock(ushort segment, uint length)
    {
        this.Segment = segment;
        this.Length = length;
    }

    public static bool operator ==(ReservedBlock blockA, ReservedBlock blockB)
    {
        if (ReferenceEquals(blockA, blockB)) {
            return true;
        }

        if (blockA is null) {
            return false;
        }

        return blockA.Equals(blockB);
    }
    public static bool operator !=(ReservedBlock blockA, ReservedBlock blockB)
    {
        if (ReferenceEquals(blockA, blockB)) {
            return false;
        }

        if (blockA is null) {
            return true;
        }

        return !blockA.Equals(blockB);
    }

    /// <summary>
    /// Gets the segment address of the reserved block.
    /// </summary>
    public ushort Segment { get; }
    /// <summary>
    /// Gets the length of the reserved block in bytes.
    /// </summary>
    public uint Length { get; }

    /// <summary>
    /// Tests for value equality with another block.
    /// </summary>
    /// <param name="other">Other block to test.</param>
    /// <returns>True if blocks are equal; otherwise false.</returns>
    public bool Equals(ReservedBlock? other)
    {
        if (other is null) {
            return false;
        }

        return this.Segment == other.Segment && this.Length == other.Length;
    }
    /// <summary>
    /// Tests for value equality with another object.
    /// </summary>
    /// <param name="other">Other object to test.</param>
    /// <returns>True if objects are equal; otherwise false.</returns>
    public override bool Equals(object? obj) => this.Equals(obj as ReservedBlock);
    /// <summary>
    /// Returns a hash code for the block.
    /// </summary>
    /// <returns>Hash code for the block.</returns>
    public override int GetHashCode() => this.Segment;
    /// <summary>
    /// Returns a string representation of the block.
    /// </summary>
    /// <returns>String representation of the block.</returns>
    public override string ToString() => $"{this.Segment:X4}: {this.Length}";
}