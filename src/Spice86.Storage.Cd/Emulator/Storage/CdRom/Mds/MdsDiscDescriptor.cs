using System.Collections.Generic;

namespace Spice86.Shared.Emulator.Storage.CdRom.Mds;

/// <summary>
/// Immutable parsed representation of an MDS file (Alcohol 120% disc descriptor).
/// Contains exactly the tracks of the first session — multi-session discs are
/// truncated to the first session to mirror dosbox-staging's
/// <c>LoadMdsFile</c> behaviour.
/// </summary>
public sealed class MdsDiscDescriptor {
    /// <summary>Initialises a new <see cref="MdsDiscDescriptor"/>.</summary>
    /// <param name="tracks">Tracks in disc order. Must be contiguous and non-empty.</param>
    public MdsDiscDescriptor(IReadOnlyList<MdsTrack> tracks) {
        ArgumentNullException.ThrowIfNull(tracks);
        Tracks = tracks;
    }

    /// <summary>Gets the parsed tracks in disc order.</summary>
    public IReadOnlyList<MdsTrack> Tracks { get; }
}
