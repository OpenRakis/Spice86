namespace Spice86.Core.Emulator.Devices.CdRom.Subchannel;

/// <summary>
/// Immutable representation of CD subchannel-Q position metadata for one disc location.
/// This is the logical payload consumed by MSCDEX IOCTL input control 0x0C
/// (audio subchannel information), and mirrors the bytes written to the caller buffer in
/// <c>Mscdex.WriteAudioSubchannel</c>.
///
/// Subchannel-Q is used by DOS CD audio clients to query where playback currently is:
/// track number and index plus both relative and absolute MSF positions.
/// </summary>
public sealed class SubchannelQData {
    /// <summary>
    /// Gets the track control+ADR attribute byte.
    /// Typical values are 0x00 for audio tracks and 0x04 for data tracks.
    /// </summary>
    public byte Attribute { get; }

    /// <summary>
    /// Gets the BCD-encoded track number (01h..99h).
    /// The lead-out track is encoded as 0xAA.
    /// </summary>
    public byte TrackNumberBcd { get; }

    /// <summary>
    /// Gets the linear (non-BCD) index number inside the track.
    /// </summary>
    public byte IndexNumber { get; }

    /// <summary>Gets the minute component of the relative MSF position within the current track.</summary>
    public byte RelativeMinute { get; }

    /// <summary>Gets the second component of the relative MSF position within the current track.</summary>
    public byte RelativeSecond { get; }

    /// <summary>Gets the frame component of the relative MSF position within the current track.</summary>
    public byte RelativeFrame { get; }

    /// <summary>
    /// Gets the minute component of the absolute disc MSF position.
    /// Includes the Red Book 150-frame pre-gap offset.
    /// </summary>
    public byte AbsoluteMinute { get; }

    /// <summary>
    /// Gets the second component of the absolute disc MSF position.
    /// Includes the Red Book 150-frame pre-gap offset.
    /// </summary>
    public byte AbsoluteSecond { get; }

    /// <summary>
    /// Gets the frame component of the absolute disc MSF position.
    /// Includes the Red Book 150-frame pre-gap offset.
    /// </summary>
    public byte AbsoluteFrame { get; }

    /// <summary>Initialises a new <see cref="SubchannelQData"/> from already-resolved field values.</summary>
    public SubchannelQData(
        byte attribute,
        byte trackNumberBcd,
        byte indexNumber,
        byte relativeMinute,
        byte relativeSecond,
        byte relativeFrame,
        byte absoluteMinute,
        byte absoluteSecond,
        byte absoluteFrame) {
        Attribute = attribute;
        TrackNumberBcd = trackNumberBcd;
        IndexNumber = indexNumber;
        RelativeMinute = relativeMinute;
        RelativeSecond = relativeSecond;
        RelativeFrame = relativeFrame;
        AbsoluteMinute = absoluteMinute;
        AbsoluteSecond = absoluteSecond;
        AbsoluteFrame = absoluteFrame;
    }
}
