namespace Spice86.Core.Emulator.Devices.CdRom.Image;

/// <summary>Extracts user data from raw CD-ROM sector encodings.</summary>
public static class SectorFraming {
    private const int CookedOffsetInRaw = 16;
    private const int CookedDataSize = 2048;
    private const int Mode2Form1OffsetInRaw = 24;

    /// <summary>
    /// Extracts the 2048 cooked data bytes from a 2352-byte raw Mode 1 sector.
    /// The user data occupies bytes 16–2063.
    /// </summary>
    /// <param name="rawSector">The full 2352-byte raw sector.</param>
    /// <param name="destination">Buffer that receives the 2048 extracted bytes.</param>
    /// <returns>2048.</returns>
    public static int ExtractCookedFromRaw2352(ReadOnlySpan<byte> rawSector, Span<byte> destination) {
        rawSector.Slice(CookedOffsetInRaw, CookedDataSize).CopyTo(destination);
        return CookedDataSize;
    }

    /// <summary>
    /// Extracts the 2048 user-data bytes from a 2352-byte raw Mode 2 Form 1 sector.
    /// The user data occupies bytes 24–2071 (after the 24-byte sync+header+subheader).
    /// </summary>
    /// <param name="rawSector">The full 2352-byte raw sector.</param>
    /// <param name="destination">Buffer that receives the 2048 extracted bytes.</param>
    /// <returns>2048.</returns>
    public static int ExtractMode2Form1FromRaw2352(ReadOnlySpan<byte> rawSector, Span<byte> destination) {
        rawSector.Slice(Mode2Form1OffsetInRaw, CookedDataSize).CopyTo(destination);
        return CookedDataSize;
    }
}
