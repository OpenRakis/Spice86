namespace Spice86.Shared.Emulator.Storage.CdRom;

/// <summary>Extracts user data from raw CD-ROM sector encodings.</summary>
public static class SectorFraming {
    private const int CookedOffsetInRaw = 16;
    private const int CookedDataSize = 2048;
    private const int Mode2Form1OffsetInRaw = 24;
    private const int RawSectorSize = 2352;

    /// <summary>
    /// Copies or converts a source sector into the caller's requested encoding.
    /// Returns 0 when the requested conversion is unsupported.
    /// </summary>
    /// <param name="sourceSector">The source sector bytes as stored by the track backend.</param>
    /// <param name="destination">Buffer that receives the requested sector payload.</param>
    /// <param name="storedMode">The on-disc encoding of <paramref name="sourceSector"/>.</param>
    /// <param name="requestedMode">The sector encoding requested by the caller.</param>
    /// <returns>The number of bytes written to <paramref name="destination"/>, or 0 when unsupported.</returns>
    public static int CopySector(ReadOnlySpan<byte> sourceSector, Span<byte> destination, CdSectorMode storedMode,
        CdSectorMode requestedMode) {
        if (requestedMode == storedMode) {
            if (destination.Length < sourceSector.Length) {
                return 0;
            }
            sourceSector.CopyTo(destination);
            return sourceSector.Length;
        }

        if (requestedMode == CdSectorMode.CookedData2048 && storedMode == CdSectorMode.Raw2352) {
            if (sourceSector.Length < RawSectorSize || destination.Length < CookedDataSize) {
                return 0;
            }
            return ExtractCookedFromRaw2352(sourceSector, destination);
        }

        if (requestedMode == CdSectorMode.CookedData2048 && storedMode == CdSectorMode.Mode2Form1) {
            if (sourceSector.Length < RawSectorSize || destination.Length < CookedDataSize) {
                return 0;
            }
            return ExtractMode2Form1FromRaw2352(sourceSector, destination);
        }

        return 0;
    }

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
