namespace Spice86.Shared.Emulator.Storage.FileSystem.Directory.LongFileName;

/// <summary>
/// Pairing of an 8.3 short directory entry with an optional long file name
/// reconstructed from any preceding VFAT LFN slots on disc.
/// </summary>
public sealed class VfatDirectoryRecord {
    /// <summary>Gets the 8.3 short entry. Never null.</summary>
    public MutableFatDirectoryEntry ShortEntry { get; }

    /// <summary>
    /// Gets the long file name reconstructed from preceding LFN slots,
    /// or an empty string when no valid LFN chain precedes the short entry.
    /// </summary>
    public string LongName { get; }

    /// <summary>Initialises a new <see cref="VfatDirectoryRecord"/>.</summary>
    /// <param name="shortEntry">The 8.3 short entry.</param>
    /// <param name="longName">The decoded long name, or an empty string when absent.</param>
    public VfatDirectoryRecord(MutableFatDirectoryEntry shortEntry, string longName) {
        ShortEntry = shortEntry;
        LongName = longName;
    }
}
