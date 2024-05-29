namespace Spice86.MemoryWrappers;
using AvaloniaHex.Document;

using Spice86.Core.Emulator.Memory;

using System;

public class MemoryBinaryDocument : IBinaryDocument {
    private readonly IMemory _memory;
    private readonly uint _startAddress;
    private readonly uint _endAddress;
    
    public MemoryBinaryDocument(IMemory memory, uint startAddress, uint endAddress) {
        _memory = memory;
        _startAddress = startAddress;
        _endAddress = endAddress;
        IsReadOnly = false;
        CanInsert = false;
        CanRemove = false;
        ValidRanges = new MemoryReadOnlyBitRangeUnion(memory, startAddress, endAddress);
    }

    public ulong Length => _endAddress - _startAddress;
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