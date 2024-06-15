﻿namespace Spice86.MemoryWrappers;
using AvaloniaHex.Document;

using Spice86.Core.Emulator.Memory;

using System;

public class MemoryBinaryDocument : IBinaryDocument {
    private readonly IMemory _memory;
    private readonly uint _startAddress;
    private readonly uint _endAddress;
    
    public MemoryBinaryDocument(IMemory memory, uint startAddress, uint endAddress) {
        IsReadOnly = false;
        CanInsert = false;
        CanRemove = false;
        _startAddress = startAddress;
        _endAddress = endAddress;
        _memory = memory;
        ValidRanges = new MemoryReadOnlyBitRangeUnion(0, _endAddress - _startAddress);
    }

    public event Action<Exception>? MemoryReadInvalidOperation;

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
        try {
            Span<byte> memRange = _memory.GetSpan((int)(_startAddress + offset), buffer.Length, false);
            memRange.CopyTo(buffer);
        } catch (InvalidOperationException e) {
            MemoryReadInvalidOperation?.Invoke(e);
        }
    }

    public void RemoveBytes(ulong offset, ulong length) {
        throw new NotSupportedException();
    }

    public void WriteBytes(ulong offset, ReadOnlySpan<byte> buffer) {
        for (int i = 0; i < buffer.Length; i++) {
            _memory.UInt8[(uint)(_startAddress + offset + (uint)i)] = buffer[i];
        }
    }
}