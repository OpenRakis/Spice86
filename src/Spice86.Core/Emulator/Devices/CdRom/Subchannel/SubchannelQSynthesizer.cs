namespace Spice86.Core.Emulator.Devices.CdRom.Subchannel;

using System.Collections.Generic;

/// <summary>
/// Computes the subchannel-Q payload for a given absolute logical block address by
/// inspecting the table of contents to locate the containing track and deriving relative
/// and absolute MSF positions. Behaviour matches DOSBox-staging's
/// <c>CDROM_Interface_Image::GetAudioSub</c>: the track-number byte is BCD-encoded, the
/// index byte is the linear value 1 (always), and both MSF triplets are written in plain
/// decimal (not BCD) so MSCDEX IOCTL 0x0C responses round-trip identically to DOSBox.
/// </summary>
public sealed class SubchannelQSynthesizer
{
    /// <summary>The standard Red Book 150-frame pre-gap offset added to LBAs to produce the absolute MSF.</summary>
    public const int RedBookPreGapFrames = 150;

    /// <summary>The well-known MSCDEX lead-out track number.</summary>
    public const int LeadOutTrackNumber = 0xAA;

    /// <summary>
    /// Computes the subchannel-Q payload for the given table of contents at the given absolute LBA.
    /// </summary>
    /// <param name="toc">The disc table of contents, ending with a synthetic lead-out entry (track 0xAA).</param>
    /// <param name="currentLba">The absolute LBA whose position should be reported.</param>
    /// <returns>The synthesised subchannel-Q data.</returns>
    public SubchannelQData Compute(IReadOnlyList<TableOfContentsEntry> toc, int currentLba)
    {
        TableOfContentsEntry? containingTrack = FindContainingTrack(toc, currentLba);

        byte attribute = 0;
        byte trackNumberBcd = 0;
        int relativeLba = 0;
        if (containingTrack != null)
        {
            attribute = containingTrack.Control;
            int trackNumber = containingTrack.TrackNumber;
            trackNumberBcd = (byte)(((trackNumber / 10) << 4) | (trackNumber % 10));
            relativeLba = currentLba - containingTrack.Lba;
            if (relativeLba < 0)
            {
                relativeLba = 0;
            }
        }

        (byte relMin, byte relSec, byte relFr) = LbaToMsf(relativeLba);
        (byte absMin, byte absSec, byte absFr) = LbaToMsf(currentLba + RedBookPreGapFrames);

        return new SubchannelQData(
            attribute: attribute,
            trackNumberBcd: trackNumberBcd,
            indexNumber: 1,
            relativeMinute: relMin,
            relativeSecond: relSec,
            relativeFrame: relFr,
            absoluteMinute: absMin,
            absoluteSecond: absSec,
            absoluteFrame: absFr);
    }

    private static TableOfContentsEntry? FindContainingTrack(IReadOnlyList<TableOfContentsEntry> toc, int lba)
    {
        for (int i = 0; i < toc.Count; i++)
        {
            TableOfContentsEntry candidate = toc[i];
            if (candidate.TrackNumber == LeadOutTrackNumber)
            {
                break;
            }
            TableOfContentsEntry? next = (i + 1 < toc.Count) ? toc[i + 1] : null;
            if (next == null || lba < next.Lba)
            {
                if (lba >= candidate.Lba)
                {
                    return candidate;
                }
            }
        }
        return null;
    }

    private static (byte min, byte sec, byte frame) LbaToMsf(int frames)
    {
        if (frames < 0)
        {
            frames = 0;
        }
        byte fr = (byte)(frames % 75);
        int totalSeconds = frames / 75;
        byte sec = (byte)(totalSeconds % 60);
        byte min = (byte)(totalSeconds / 60);
        return (min, sec, fr);
    }
}
