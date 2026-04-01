namespace Spice86.Core.Emulator.Devices.Video;

/// <summary>
///     Provides a two-parameter indexer over the flat interleaved VRAM buffer,
///     preserving the <c>Planes[plane, address]</c> calling syntax.
/// </summary>
public sealed class PlaneAccessor {
    private readonly byte[] _vram;

    /// <summary>
    ///     Creates a new accessor backed by the given interleaved VRAM buffer.
    /// </summary>
    /// <param name="vram">The flat interleaved VRAM array where plane p at VGA address a is at index a * 4 + p.</param>
    internal PlaneAccessor(byte[] vram) {
        _vram = vram;
    }

    /// <summary>
    ///     Gets or sets the byte at the specified plane and VGA address.
    /// </summary>
    /// <param name="plane">The plane index (0–3).</param>
    /// <param name="address">The VGA address within the plane.</param>
    public byte this[int plane, int address] {
        get => _vram[address * 4 + plane];
        set => _vram[address * 4 + plane] = value;
    }
}
