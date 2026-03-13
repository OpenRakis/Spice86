namespace Spice86.ViewModels.DataModels;

using AvaloniaHex.Document;

using Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;
using Spice86.Core.Emulator.Memory;

/// <inheritdoc cref="IBinaryDocument" />
public sealed class EmmPageBinaryDocument : IBinaryDocument {
    private readonly EmmPage _emmPage;

    public EmmPageBinaryDocument(EmmPage emmPage) {
        _emmPage = emmPage;
        IsReadOnly = true;
        CanInsert = false;
        CanRemove = false;
        ValidRanges = new MemoryReadOnlyBitRangeUnion(0, _emmPage.Size);
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

    public ulong Length => _emmPage.Size;
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
        if (startOffset >= _emmPage.Size) {
            return;
        }
        int readableLength = (int)Math.Min((ulong)buffer.Length, _emmPage.Size - startOffset);
        for (int index = 0; index < readableLength; index++) {
            buffer[index] = _emmPage.Read(startOffset + (uint)index);
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
