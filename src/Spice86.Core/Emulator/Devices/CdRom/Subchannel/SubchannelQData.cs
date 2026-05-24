namespace Spice86.Core.Emulator.Devices.CdRom.Subchannel;

/// <summary>
/// Immutable result of subchannel-Q synthesis for a given absolute logical block address.
/// The byte fields mirror the MSCDEX IOCTL 0x0C response wire layout: the attribute byte,
/// the BCD-encoded track number, the linear index number, and two MSF triplets (one
/// relative to the current track, one absolute on the disc) expressed as plain decimal
/// values per DOSBox-staging parity.
/// </summary>
public sealed class SubchannelQData
{
    /// <summary>Gets the control+ADR attribute byte for the containing track (0 for audio, 4 for data).</summary>
    public byte Attribute { get; }

    /// <summary>Gets the BCD-encoded track number (1..99). The lead-out track is reported as 0xAA.</summary>
    public byte TrackNumberBcd { get; }

    /// <summary>Gets the linear (non-BCD) index number within the current track (1 outside pregap).</summary>
    public byte IndexNumber { get; }

    /// <summary>Gets the minute component of the relative MSF position within the current track.</summary>
    public byte RelativeMinute { get; }

    /// <summary>Gets the second component of the relative MSF position within the current track.</summary>
    public byte RelativeSecond { get; }

    /// <summary>Gets the frame component of the relative MSF position within the current track.</summary>
    public byte RelativeFrame { get; }

    /// <summary>Gets the minute component of the absolute MSF position on the disc (includes 150-frame pregap).</summary>
    public byte AbsoluteMinute { get; }

    /// <summary>Gets the second component of the absolute MSF position on the disc (includes 150-frame pregap).</summary>
    public byte AbsoluteSecond { get; }

    /// <summary>Gets the frame component of the absolute MSF position on the disc (includes 150-frame pregap).</summary>
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
        byte absoluteFrame)
    {
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
