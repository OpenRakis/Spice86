using System.Collections.Generic;

namespace Spice86.Core.Emulator.Devices.CdRom.Image;

/// <summary>Holds the fully parsed contents of a CUE sheet file.</summary>
public sealed class CueSheet {
    /// <summary>Gets the list of parsed CUE entries (one per INDEX directive, carrying track/file context).</summary>
    public IReadOnlyList<CueEntry> Entries { get; }

    /// <summary>Gets the CATALOG string from the CUE sheet, or <see langword="null"/> if absent.</summary>
    public string? Catalog { get; }

    /// <summary>Initialises a new <see cref="CueSheet"/>.</summary>
    public CueSheet(IReadOnlyList<CueEntry> entries, string? catalog) {
        Entries = entries;
        Catalog = catalog;
    }
}
