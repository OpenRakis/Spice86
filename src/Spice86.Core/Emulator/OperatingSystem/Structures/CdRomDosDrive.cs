namespace Spice86.Core.Emulator.OperatingSystem.Structures;

/// <summary>
/// Represents a DOS-visible CD-ROM drive letter.
/// </summary>
public sealed class CdRomDosDrive : DosDriveBase, IDosPathContent {
    public IDosPathContent? ImageContent { get; init; }
    /// <summary>
    /// Initializes a new <see cref="CdRomDosDrive"/>.
    /// </summary>
    public CdRomDosDrive() {
        IsRemovable = true;
    }

    public bool FileExists(string relativePath) => ImageContent?.FileExists(relativePath) ?? false;

    public bool DirectoryExists(string relativePath) => ImageContent?.DirectoryExists(relativePath) ?? false;

    public IReadOnlyList<DosContentEntry> GetDirectoryEntries(string relativePath) =>
        ImageContent?.GetDirectoryEntries(relativePath) ?? Array.Empty<DosContentEntry>();

    public bool TryOpenRead(string relativePath, out Stream? stream) {
        stream = null;
        return ImageContent is not null && ImageContent.TryOpenRead(relativePath, out stream);
    }
}