namespace Spice86.Tests.CdRom.Subchannel;

using FluentAssertions;

using Spice86.Core.Emulator.Devices.CdRom;
using Spice86.Core.Emulator.Devices.CdRom.Subchannel;

using System;
using System.Collections.Generic;

using Xunit;

/// <summary>
/// Behavioural tests for <see cref="SubchannelQSynthesizer"/>. All field formats follow
/// DOSBox-staging parity: the track byte is BCD-encoded, the index byte is the linear
/// value 1, and both MSF triplets are plain decimal (not BCD).
/// </summary>
public sealed class SubchannelQSynthesizerTests
{
    private const byte AudioControl = 0x00;
    private const byte DataControl = 0x04;

    private static TableOfContentsEntry AudioTrack(int trackNumber, int lba)
        => new(trackNumber, lba, isAudio: true, control: AudioControl, adr: 1);

    private static TableOfContentsEntry DataTrack(int trackNumber, int lba)
        => new(trackNumber, lba, isAudio: false, control: DataControl, adr: 1);

    private static TableOfContentsEntry LeadOut(int lba)
        => new(0xAA, lba, isAudio: false, control: DataControl, adr: 1);

    /// <summary>
    /// LBA 0 sits at the start of track 1: relative MSF must be 00:00:00 and absolute MSF
    /// must be 00:02:00 (Red Book 150-frame pregap).
    /// </summary>
    [Fact]
    public void Compute_AtStartOfFirstTrack_ReturnsZeroRelativeAndPregapAbsolute()
    {
        // Arrange
        SubchannelQSynthesizer synthesizer = new();
        List<TableOfContentsEntry> toc = new() {
            AudioTrack(1, lba: 0),
            LeadOut(lba: 100)
        };

        // Act
        SubchannelQData result = synthesizer.Compute(toc, currentLba: 0);

        // Assert
        result.TrackNumberBcd.Should().Be(0x01);
        result.IndexNumber.Should().Be(1);
        result.Attribute.Should().Be(AudioControl);
        result.RelativeMinute.Should().Be(0);
        result.RelativeSecond.Should().Be(0);
        result.RelativeFrame.Should().Be(0);
        result.AbsoluteMinute.Should().Be(0);
        result.AbsoluteSecond.Should().Be(2);
        result.AbsoluteFrame.Should().Be(0);
    }

    /// <summary>
    /// One full second into track 1 (LBA 75) the relative MSF must be 00:01:00 and the
    /// absolute MSF must be 00:03:00 (00:01:00 + 150-frame pregap).
    /// </summary>
    [Fact]
    public void Compute_OneSecondIntoTrack_ReturnsOneSecondRelativeAndOneSecondPlusPregapAbsolute()
    {
        // Arrange
        SubchannelQSynthesizer synthesizer = new();
        List<TableOfContentsEntry> toc = new() {
            AudioTrack(1, lba: 0),
            LeadOut(lba: 1000)
        };

        // Act
        SubchannelQData result = synthesizer.Compute(toc, currentLba: 75);

        // Assert
        result.RelativeMinute.Should().Be(0);
        result.RelativeSecond.Should().Be(1);
        result.RelativeFrame.Should().Be(0);
        result.AbsoluteMinute.Should().Be(0);
        result.AbsoluteSecond.Should().Be(3);
        result.AbsoluteFrame.Should().Be(0);
    }

    /// <summary>
    /// Track numbers are BCD-encoded so track 12 must produce byte 0x12, not 0x0C.
    /// </summary>
    [Fact]
    public void Compute_DoubleDigitTrackNumber_EncodesAsBcd()
    {
        // Arrange
        SubchannelQSynthesizer synthesizer = new();
        List<TableOfContentsEntry> toc = new() {
            AudioTrack(12, lba: 0),
            LeadOut(lba: 500)
        };

        // Act
        SubchannelQData result = synthesizer.Compute(toc, currentLba: 10);

        // Assert
        result.TrackNumberBcd.Should().Be(0x12);
    }

    /// <summary>
    /// Data tracks must report the data control nibble (0x04). Audio tracks report 0x00.
    /// </summary>
    [Fact]
    public void Compute_DataTrack_ReportsDataAttribute()
    {
        // Arrange
        SubchannelQSynthesizer synthesizer = new();
        List<TableOfContentsEntry> toc = new() {
            DataTrack(1, lba: 0),
            AudioTrack(2, lba: 500),
            LeadOut(lba: 1000)
        };

        // Act
        SubchannelQData inData = synthesizer.Compute(toc, currentLba: 100);
        SubchannelQData inAudio = synthesizer.Compute(toc, currentLba: 600);

        // Assert
        inData.Attribute.Should().Be(DataControl);
        inData.TrackNumberBcd.Should().Be(0x01);
        inAudio.Attribute.Should().Be(AudioControl);
        inAudio.TrackNumberBcd.Should().Be(0x02);
    }

    /// <summary>
    /// When the absolute LBA falls inside track 2, the relative MSF must be measured from
    /// track 2's start, not from disc origin.
    /// </summary>
    [Fact]
    public void Compute_InsideSecondTrack_RelativeMsfIsMeasuredFromTrackStart()
    {
        // Arrange
        SubchannelQSynthesizer synthesizer = new();
        List<TableOfContentsEntry> toc = new() {
            AudioTrack(1, lba: 0),
            AudioTrack(2, lba: 75 * 60), // 1 minute in
            LeadOut(lba: 75 * 60 * 2)
        };

        // Act
        SubchannelQData result = synthesizer.Compute(toc, currentLba: 75 * 60 + 150);

        // Assert
        result.TrackNumberBcd.Should().Be(0x02);
        result.RelativeMinute.Should().Be(0);
        result.RelativeSecond.Should().Be(2);
        result.RelativeFrame.Should().Be(0);
        result.AbsoluteMinute.Should().Be(1);
        result.AbsoluteSecond.Should().Be(4); // 1 minute + 2 seconds + 2-second pregap
        result.AbsoluteFrame.Should().Be(0);
    }

    /// <summary>
    /// LBAs that fall outside any regular track (e.g. lead-out region) yield a zero
    /// track/attribute/relative position, while the absolute MSF still reflects the LBA.
    /// </summary>
    [Fact]
    public void Compute_LbaInLeadOut_ReturnsZeroTrackAndAttribute()
    {
        // Arrange
        SubchannelQSynthesizer synthesizer = new();
        List<TableOfContentsEntry> toc = new() {
            AudioTrack(1, lba: 0),
            LeadOut(lba: 500)
        };

        // Act
        SubchannelQData result = synthesizer.Compute(toc, currentLba: 600);

        // Assert
        result.TrackNumberBcd.Should().Be(0);
        result.Attribute.Should().Be(0);
        result.RelativeMinute.Should().Be(0);
        result.RelativeSecond.Should().Be(0);
        result.RelativeFrame.Should().Be(0);
        result.AbsoluteSecond.Should().Be(10); // (600 + 150) / 75 = 10 seconds
    }

    /// <summary>
    /// Frame-level precision: relative LBA 74 (just below 1 second) produces frame 74.
    /// </summary>
    [Fact]
    public void Compute_FrameLevelPrecision_PreservesFrameComponent()
    {
        // Arrange
        SubchannelQSynthesizer synthesizer = new();
        List<TableOfContentsEntry> toc = new() {
            AudioTrack(1, lba: 0),
            LeadOut(lba: 1000)
        };

        // Act
        SubchannelQData result = synthesizer.Compute(toc, currentLba: 74);

        // Assert
        result.RelativeMinute.Should().Be(0);
        result.RelativeSecond.Should().Be(0);
        result.RelativeFrame.Should().Be(74);
    }

    /// <summary>
    /// Index byte must always be the linear value 1 per DOSBox-staging
    /// <c>MSCDEX_IOCTL_Input</c> case 0x0C parity.
    /// </summary>
    [Fact]
    public void Compute_IndexIsAlwaysOne()
    {
        // Arrange
        SubchannelQSynthesizer synthesizer = new();
        List<TableOfContentsEntry> toc = new() {
            AudioTrack(1, lba: 0),
            AudioTrack(2, lba: 500),
            LeadOut(lba: 1000)
        };

        // Act
        SubchannelQData inTrack1 = synthesizer.Compute(toc, currentLba: 100);
        SubchannelQData inTrack2 = synthesizer.Compute(toc, currentLba: 700);

        // Assert
        inTrack1.IndexNumber.Should().Be(1);
        inTrack2.IndexNumber.Should().Be(1);
    }
}
