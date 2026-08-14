namespace Spice86.ViewModels.DataModels;

using AvaloniaHex.Document;

using Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;

/// <inheritdoc cref="IBinaryDocument" />
public sealed class XmsBlockBinaryDocument : IBinaryDocument {
    private readonly ExtendedMemoryManager _xms;
    private readonly uint _blockOffset;
    private readonly uint _blockLength;

    public XmsBlockBinaryDocument(ExtendedMemoryManager xms, uint blockOffset, uint blockLength) {
        _xms = xms;
        _blockOffset = blockOffset;
        _blockLength = blockLength;
        IsReadOnly = true;
        CanInsert = false;
        CanRemove = false;
        ValidRanges = new MemoryReadOnlyBitRangeUnion(0, _blockLength);
    }

    public event Action<Exception>? MemoryReadInvalidOperation {
        add {
        }
        remove {
        }
    }

    public event EventHandler<BinaryDocumentChange>? Changed {
        add {
        }
        remove {
        }
    }

    public ulong Length => _blockLength;
    public bool IsReadOnly { get; }
    public bool CanInsert { get; }
    public bool CanRemove { get; }
    public IReadOnlyBitRangeUnion ValidRanges { get; }

    public void InsertBytes(ulong offset, ReadOnlySpan<byte> buffer) {
        throw new NotSupportedException();
    }

    public void ReadBytes(ulong offset, Span<byte> buffer) {
        if (buffer.Length == 0) {
            return;
        }
        uint startOffset = (uint)offset;
        if (startOffset >= _blockLength) {
            return;
        }
        int readableLength = (int)Math.Min((ulong)buffer.Length, _blockLength - startOffset);
        IList<byte> blockSlice = _xms.GetSlice(_blockOffset + startOffset, readableLength);
        for (int index = 0; index < readableLength; index++) {
            buffer[index] = blockSlice[index];
        }
    }

    public void RemoveBytes(ulong offset, ulong length) {
        throw new NotSupportedException();
    }

    public void WriteBytes(ulong address, ReadOnlySpan<byte> buffer) {
        throw new NotSupportedException();
    }

    public void Flush() {
    }

    public void Dispose() {
    }
}