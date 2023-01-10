namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;

using System;

/// <summary>
/// Represents a block of XMS memory.
/// </summary>
public readonly struct XmsBlock : IEquatable<XmsBlock> {
    public XmsBlock(int handle, uint offset, uint length, bool used) {
        Handle = handle;
        Offset = offset;
        Length = length;
        IsUsed = used;
    }

    /// <summary>
    /// Gets the handle which owns the block.
    /// </summary>
    public int Handle { get; }

    /// <summary>
    /// Gets the offset of the block from the XMS base address.
    /// </summary>
    public uint Offset { get; }

    /// <summary>
    /// Gets the length of the block in bytes.
    /// </summary>
    public uint Length { get; }

    /// <summary>
    /// Gets a value indicating whether the block is in use.
    /// </summary>
    public bool IsUsed { get; }

    public override string ToString() {
        if (IsUsed) {
            return $"{Handle:X4}: {Offset:X8} to {Offset + Length:X8}";
        } else {
            return "Free";
        }
    }

    public override bool Equals(object? obj) => obj is XmsBlock b && Equals(b);

    public override int GetHashCode() => Handle ^ (int)Offset ^ (int)Length;

    public bool Equals(XmsBlock other) => Handle == other.Handle && Offset == other.Offset && Length == other.Length && IsUsed == other.IsUsed;

    /// <summary>
    /// Allocates a block of memory from a free block.
    /// </summary>
    /// <param name="handle">Handle making the allocation.</param>
    /// <param name="length">Length of the requested block in bytes.</param>
    /// <returns>Array of blocks to replace this block.</returns>
    public XmsBlock[] Allocate(int handle, uint length) {
        if (IsUsed) {
            throw new InvalidOperationException();
        }

        if (length > Length) {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        if (length == Length) {
            return new XmsBlock[] { new XmsBlock(handle, Offset, length, true) };
        }

        var blocks = new XmsBlock[2];

        blocks[0] = new XmsBlock(handle, Offset, length, true);
        blocks[1] = new XmsBlock(0, Offset + length, Length - length, false);

        return blocks;
    }

    /// <summary>
    /// Frees a used block of memory.
    /// </summary>
    /// <returns>Freed block to replace this block.</returns>
    public XmsBlock Free() => new(0, Offset, Length, false);

    /// <summary>
    /// Merges two contiguous unused blocks of memory.
    /// </summary>
    /// <param name="other">Other unused block to merge with.</param>
    /// <returns>Merged block of memory.</returns>
    public XmsBlock Join(XmsBlock other) {
        if (IsUsed || other.IsUsed) {
            throw new InvalidOperationException();
        }

        if (Offset + Length != other.Offset) {
            throw new ArgumentException($"{nameof(other)} was not joinable", nameof(other));
        }

        return new XmsBlock(0, Offset, Length + other.Length, false);
    }

    public static bool operator ==(XmsBlock left, XmsBlock right) {
        return left.Equals(right);
    }

    public static bool operator !=(XmsBlock left, XmsBlock right) {
        return !(left == right);
    }
}
