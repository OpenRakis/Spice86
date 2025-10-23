namespace Spice86.ViewModels.DataModels;

using AvaloniaHex.Document;

using Spice86.Core.Emulator.Memory;

/// <inheritdoc cref="IBinaryDocument" />
public sealed class DataMemoryDocument : IBinaryDocument {
    private readonly IMemory _memory;
    private readonly uint _startAddress;
    private readonly uint _endAddress;

    public DataMemoryDocument(IMemory memory, uint startAddress, uint endAddress) {
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

#pragma warning disable CS0067 // Binary document is write-through; UI listens to memory polling instead
    public event EventHandler<BinaryDocumentChange>? Changed;
#pragma warning restore CS0067

    public void InsertBytes(ulong offset, ReadOnlySpan<byte> buffer) {
        throw new NotSupportedException();
    }

    public void ReadBytes(ulong offset, Span<byte> buffer) {
        if (buffer.Length == 0) {
            return;
        }
        try {
            Span<byte> memRange = _memory.ReadRam((uint)buffer.Length, (uint)(_startAddress + offset));
            memRange.CopyTo(buffer);
        } catch (Exception e) when (e is ArgumentException or InvalidOperationException) {
            MemoryReadInvalidOperation?.Invoke(e);
        }
    }

    public void RemoveBytes(ulong offset, ulong length) {
        throw new NotSupportedException();
    }

    public void WriteBytes(ulong address, ReadOnlySpan<byte> buffer) {
        _memory.WriteRam(buffer.ToArray(), (uint)address);
    }

    /// <summary>
    /// Does nothing
    /// </summary>
    public void Flush() {
        //NOP
    }

    /// <summary>
    /// Does nothing
    /// </summary>
    public void Dispose() {
        //NOP
    }
}