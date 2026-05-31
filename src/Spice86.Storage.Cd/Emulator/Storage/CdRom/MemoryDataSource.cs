namespace Spice86.Shared.Emulator.Storage.CdRom;

using System;

/// <summary>Provides <see cref="IDataSource"/> access backed by an in-memory byte array.</summary>
public sealed class MemoryDataSource : IDataSource {
    private readonly byte[] _data;

    /// <summary>Initialises a new <see cref="MemoryDataSource"/> wrapping <paramref name="data"/>.</summary>
    /// <param name="data">The raw bytes to expose as a data source.</param>
    public MemoryDataSource(byte[] data) {
        _data = data;
    }

    /// <inheritdoc/>
    public long LengthBytes => _data.Length;

    /// <inheritdoc/>
    public int Read(long byteOffset, Span<byte> destination) {
        if (byteOffset < 0 || byteOffset > int.MaxValue || byteOffset >= _data.Length) {
            return 0;
        }

        int available = (int)Math.Min(destination.Length, _data.Length - byteOffset);
        if (available <= 0) {
            return 0;
        }

        _data.AsSpan((int)byteOffset, available).CopyTo(destination);
        return available;
    }
}
