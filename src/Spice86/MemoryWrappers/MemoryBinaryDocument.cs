namespace Spice86.MemoryWrappers;
using AvaloniaHex.Document;

using Spice86.Core.Emulator.Memory;

using System;

public class MemoryBinaryDocument : IBinaryDocument {
    private readonly IMemory _memory;
    public MemoryBinaryDocument(IMemory memory) {
        _memory = memory;
        IsReadOnly = false;
        CanInsert = false;
        CanRemove = false;
        ValidRanges = new MemoryReadOnlyBitRangeUnion(memory);
    }

    public ulong Length => _memory.Length;
    public bool IsReadOnly { get; }
    public bool CanInsert { get; }
    public bool CanRemove { get; }
    public IReadOnlyBitRangeUnion ValidRanges { get; }

    public event EventHandler<BinaryDocumentChange>? Changed;

    public void InsertBytes(ulong offset, ReadOnlySpan<byte> buffer) {
        throw new NotSupportedException();
    }

    public void ReadBytes(ulong offset, Span<byte> buffer) {
        for (int i = 0; i < buffer.Length; i++) {
            buffer[i] = _memory[(uint)(offset + (uint)i)];
        }
    }

    public void RemoveBytes(ulong offset, ulong length) {
        throw new NotSupportedException();
    }

    public void WriteBytes(ulong offset, ReadOnlySpan<byte> buffer) {
        for (int i = 0; i < buffer.Length; i++) {
            _memory[(uint)(offset + (uint)i)] = buffer[i];
        }
    }
}