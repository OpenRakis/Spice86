namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

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
        Segment = segment;
        Length = length;
        IsUsed = isUsed;
    }

    public bool Equals(Allocation? other) {
        if (other is null) {
            return false;
        }

        return Segment == other.Segment && IsUsed == other.IsUsed && Length == other.Length;
    }

    public override bool Equals(object? obj) => Equals(obj as Allocation);

    public override int GetHashCode() => Segment;

    public override string ToString() => $"{Segment:X4}: {Length}";
}
