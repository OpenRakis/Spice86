namespace Spice86.Core.Emulator.Devices.CdRom.Subchannel;

using System.Collections.Generic;

/// <summary>
/// Computes the subchannel-Q payload for a given absolute logical block address by
/// inspecting the table of contents to locate the containing track and deriving relative
/// and absolute MSF positions. Behaviour matches DOSBox-staging's
/// <c>CDROM_Interface_Image::GetAudioSub</c>: the track-number byte is BCD-encoded, the
/// index byte is the linear value 1 (always), and both MSF triplets are written in plain
/// decimal (not BCD) so MSCDEX IOCTL 0x0C responses round-trip identically to DOSBox.
/// The result is returned as <see cref="SubchannelQData"/> and then serialized by
/// MSCDEX into the caller-visible IOCTL buffer.
/// </summary>
public sealed class SubchannelQSynthesizer {
    /// <summary>The standard Red Book 150-frame pre-gap offset added to LBAs to produce the absolute MSF.</summary>
    public const int RedBookPreGapFrames = 150;

    /// <summary>The well-known MSCDEX lead-out track number.</summary>
    public const int LeadOutTrackNumber = 0xAA;

    /// <summary>
    /// Computes a <see cref="SubchannelQData"/> snapshot for a single absolute disc position.
    /// </summary>
    /// <param name="toc">
    /// Disc table of contents ordered by track start LBA and ending with a synthetic
    /// lead-out entry (track 0xAA).
    /// </param>
    /// <param name="currentLba">Absolute logical block address to report.</param>
    /// <returns>
    /// Subchannel-Q metadata containing attribute, BCD track number, index, relative MSF,
    /// and absolute MSF values.
    /// </returns>
    public SubchannelQData Compute(IReadOnlyList<TableOfContentsEntry> toc, int currentLba) {
        TrackLookup containingTrackLookup = FindContainingTrack(toc, currentLba);

        byte attribute = 0;
        byte trackNumberBcd = 0;
        int relativeLba = 0;
        if (containingTrackLookup.IsPresent) {
            TableOfContentsEntry containingTrack = toc[containingTrackLookup.Index];
            attribute = containingTrack.Control;
            int trackNumber = containingTrack.TrackNumber;
            trackNumberBcd = (byte)(((trackNumber / 10) << 4) | (trackNumber % 10));
            relativeLba = currentLba - containingTrack.Lba;
            if (relativeLba < 0) {
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

    private static TrackLookup FindContainingTrack(IReadOnlyList<TableOfContentsEntry> toc, int lba) {
        for (int i = 0; i < toc.Count; i++) {
            TableOfContentsEntry candidate = toc[i];
            if (candidate.TrackNumber == LeadOutTrackNumber) {
                break;
            }
            bool hasNext = i + 1 < toc.Count;
            if (!hasNext || lba < toc[i + 1].Lba) {
                if (lba >= candidate.Lba) {
                    return TrackLookup.From(i);
                }
            }
        }
        return TrackLookup.None;
    }

    private readonly record struct TrackLookup(bool IsPresent, int Index) {
        public static TrackLookup None { get; } = new(false, -1);

        public static TrackLookup From(int index) {
            return new TrackLookup(true, index);
        }
    }

    private static (byte min, byte sec, byte frame) LbaToMsf(int frames) {
        if (frames < 0) {
            frames = 0;
        }
        byte fr = (byte)(frames % 75);
        int totalSeconds = frames / 75;
        byte sec = (byte)(totalSeconds % 60);
        byte min = (byte)(totalSeconds / 60);
        return (min, sec, fr);
    }
}
