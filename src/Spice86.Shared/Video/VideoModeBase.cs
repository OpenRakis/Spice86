namespace Spice86.Shared.Video;

/// <summary>
/// Base class for any video mode
/// </summary>
public abstract class VideoModeBase {
    /// <summary>
    /// The value sent in the AL register. <be/>
    /// For example, 0x13 for VGA, 320x200. Byte value.
    /// </summary>
    public VideoModeIdentifier Id { get; protected set; }

    public int Width { get; protected set; }
    public int Height { get; protected set; }

    public bool IsPlanar { get; protected set; }

    /// <summary>
    /// For example, 0xA000 for VGA mode 0x13
    /// </summary>
    public uint PhysicalAddress { get; protected set; }
}