namespace Spice86.Core.Emulator.Memory;

using System;

/// <summary>
/// Describes a conventional memory allocation.
/// </summary>
public class Allocation : IEquatable<Allocation> {
    /// <summary>
    /// The starting segment of the allocation.
    /// </summary>
    public ushort Segment;
    /// <summary>
    /// Indicates whether the allocation is in use or a free block.
    /// </summary>
    public bool IsUsed;
    /// <summary>
    /// The length of the allocation in 16-byte paragraphs.
    /// </summary>
    public uint Length;

    /// <summary>
    /// Initializes a new instance of the Allocation class.
    /// </summary>
    /// <param name="segment">The starging segment of the allocation.</param>
    /// <param name="length">The length of the allocation in 16-byte paragraphs.</param>
    /// <param name="isUsed">Indicates whether the allocation is in use or a free block.</param>
    public Allocation(ushort segment, uint length, bool isUsed) {
        this.Segment = segment;
        this.Length = length;
        this.IsUsed = isUsed;
    }

    public bool Equals(Allocation? other) {
        if (other is null) {
            return false;
        }

        return this.Segment == other.Segment && this.IsUsed == other.IsUsed && this.Length == other.Length;
    }
    public override bool Equals(object? obj) => Equals(obj as Allocation);
    public override int GetHashCode() => this.Segment;
    public override string ToString() => $"{this.Segment:X4}: {this.Length}";
}
