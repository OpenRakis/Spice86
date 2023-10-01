namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;

using System;

/// <summary>
/// Represents a block of XMS memory.
/// </summary>
public class XmsBlock : MemoryBasedDataStructure, IEquatable<XmsBlock> {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    public XmsBlock(int Handle, uint Offset, uint Length, bool IsUsed, IByteReaderWriter byteReaderWriter, uint baseAddress) : base(byteReaderWriter, baseAddress) {
        this.Handle = Handle;
        this.Offset = Offset;
        this.Length = Length;
        this.IsUsed = IsUsed;
    }

    public XmsBlock(XmsBlock other) : base(other.ByteReaderWriter, other.BaseAddress) {
        this.Handle = other.Handle;
        this.Offset = other.Offset;
        this.Length = other.Length;
        this.IsUsed = other.IsUsed;
    }

    /// <inheritdoc />
    public override string ToString() {
        if (IsUsed) {
            return $"{Handle:X4}: {Offset:X8} to {Offset + Length:X8}";
        } else {
            return "Free";
        }
    }

    /// <inheritdoc />
    public override int GetHashCode() {
        return HashCode.Combine(Handle, Offset, Length, IsUsed);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) {
        if (ReferenceEquals(null, obj)) {
            return false;
        }

        if (ReferenceEquals(this, obj)) {
            return true;
        }

        if (obj.GetType() != this.GetType()) {
            return false;
        }

        return Equals((XmsBlock)obj);
    }

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
            return new XmsBlock[] { new XmsBlock(handle, Offset, length, true, ByteReaderWriter, BaseAddress) };
        }

        var blocks = new XmsBlock[2];

        blocks[0] = new XmsBlock(handle, Offset, length, true, ByteReaderWriter, BaseAddress);
        blocks[1] = new XmsBlock(0, Offset + length, Length - length, false, ByteReaderWriter, BaseAddress);

        return blocks;
    }

    /// <summary>
    /// Frees a used block of memory.
    /// </summary>
    /// <returns>Freed block to replace this block.</returns>
    public XmsBlock Free() => new(0, Offset, Length, false, ByteReaderWriter, BaseAddress);

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
            throw new ArgumentException($"{nameof(other)} could not be joined", nameof(other));
        }

        return new XmsBlock(0, Offset, Length + other.Length, false, ByteReaderWriter, BaseAddress);
    }

    public int Handle { get; init; }
    public uint Offset { get; init; }
    public uint Length { get; init; }
    public bool IsUsed { get; init; }

    public bool Equals(XmsBlock? other)
    {
        if (ReferenceEquals(null, other)) {
            return false;
        }

        if (ReferenceEquals(this, other)) {
            return true;
        }

        return Handle == other.Handle && Offset == other.Offset && Length == other.Length && IsUsed == other.IsUsed;
    }
}
