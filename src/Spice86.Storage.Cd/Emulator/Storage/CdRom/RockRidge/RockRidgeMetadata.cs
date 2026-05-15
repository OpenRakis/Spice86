namespace Spice86.Shared.Emulator.Storage.CdRom.RockRidge;

/// <summary>
/// Immutable POSIX/RRIP metadata extracted from the System Use Area of an ISO 9660
/// directory record. All fields are nullable: a null value means the corresponding
/// SUSP entry (NM or PX) was not present.
/// </summary>
public sealed class RockRidgeMetadata
{
    /// <summary>Gets the alternate (long) name from one or more NM entries, or null if absent.</summary>
    public string? AlternateName { get; }

    /// <summary>Gets the POSIX file mode from the PX entry, or null if absent.</summary>
    public uint? PosixFileMode { get; }

    /// <summary>Gets the POSIX file link count from the PX entry, or null if absent.</summary>
    public uint? FileLinkCount { get; }

    /// <summary>Gets the POSIX user id from the PX entry, or null if absent.</summary>
    public uint? UserId { get; }

    /// <summary>Gets the POSIX group id from the PX entry, or null if absent.</summary>
    public uint? GroupId { get; }

    /// <summary>Gets a value indicating whether any Rock Ridge field was populated.</summary>
    public bool HasAny => AlternateName != null
                          || PosixFileMode.HasValue
                          || FileLinkCount.HasValue
                          || UserId.HasValue
                          || GroupId.HasValue;

    /// <summary>Initialises a new <see cref="RockRidgeMetadata"/> with all fields.</summary>
    public RockRidgeMetadata(
        string? alternateName,
        uint? posixFileMode,
        uint? fileLinkCount,
        uint? userId,
        uint? groupId)
    {
        AlternateName = alternateName;
        PosixFileMode = posixFileMode;
        FileLinkCount = fileLinkCount;
        UserId = userId;
        GroupId = groupId;
    }
}
