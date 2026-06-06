namespace Spice86.Tests.Dos;

using System;
using System.Collections.Generic;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.Devices.CdRom;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Shared.Emulator.Storage.CdRom;
using Spice86.Shared.Interfaces;
using Spice86.Tests.Utility;

using Xunit;

public class CdRomParityTests {
    [Fact]
    public void VirtualIsoImage_Read_RawModeOnCookedTrack_ReturnsZero() {
        // Arrange
        using TempFile tempFile = new("cdrom-parity");
        using VirtualIsoImage image = CdRomTestFixture.CreateVirtualIsoImage(tempFile);
        byte[] buffer = new byte[2352];

        // Act
        int bytesRead = image.Read(16, buffer, CdSectorMode.Raw2352);

        // Assert
        bytesRead.Should().Be(0,
            "reject raw 2352-byte reads from cooked-only virtual ISO tracks");
    }

    [Fact]
    public void IsoImage_Read_RawModeOnCookedTrack_ReturnsZero() {
        // Arrange
        using TempFile tempFile = new("cdrom-parity");
        string isoPath = CdRomTestFixture.CreateIsoFile(tempFile);
        using IsoImage image = new(isoPath);
        byte[] buffer = new byte[2352];

        // Act
        int bytesRead = image.Read(16, buffer, CdSectorMode.Raw2352);

        // Assert
        bytesRead.Should().Be(0,
            "reject raw 2352-byte reads from plain ISO images because they only expose cooked 2048-byte sectors");
    }

    [Fact]
    public void CdRomDrive_Eject_DoesNotOpenDoorOrFlagMediaChangedForMountedImages() {
        // Arrange
        using TempFile tempFile = new("cdrom-parity");
        using VirtualIsoImage image = CdRomTestFixture.CreateVirtualIsoImage(tempFile);
        ISoundChannelCreator channelCreator = Substitute.For<ISoundChannelCreator>();
        channelCreator
            .AddChannel(Arg.Any<Action<int>>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<HashSet<ChannelFeature>>())
            .Returns(callInfo => new SoundChannel((Action<int>)callInfo[0], (string)callInfo[2], (HashSet<ChannelFeature>)callInfo[3]));
        IDriveActivityNotifier activityNotifier = Substitute.For<IDriveActivityNotifier>();
        CdRomDrive drive = new(image, channelCreator, activityNotifier, driveLetter: 'D');
        bool initialMediaChanged = drive.MediaState.ReadAndClearMediaChanged();

        // Act
        drive.Eject();
        bool postEjectMediaChanged = drive.MediaState.ReadAndClearMediaChanged();

        // Assert
        initialMediaChanged.Should().BeTrue(
            "newly mounted images should expose the initial media-changed notification before the eject check runs");
        drive.MediaState.IsDoorOpen.Should().BeFalse(
            "image-backed drives do not transition to an open tray state on eject requests");
        postEjectMediaChanged.Should().BeFalse(
            "image-backed drives treat eject as a no-op rather than a media change");
    }

    [Fact]
    public void CdRomDrive_AudioCallback_StopsAtPlaybackEndAndIgnoresIncompleteTrailingSectorReads() {
        // Arrange/Act/Assert (playback end)
        using (TempFile playbackEndTempFile = new("cdrom-parity-audio-playback-end")) {
            CdRomDrive playbackEndDrive = CdRomTestFixture.CreateAudioCueBinDrive(
                playbackEndTempFile,
                CdRomTestFixture.CreateAudioDiscBytes(1),
                out ICdRomImage playbackEndImage,
                out Action<int> playbackEndCallback,
                out _);
            using (playbackEndImage) {
                playbackEndDrive.PlayAudio(0, 1);
                playbackEndCallback(588 * 2);
                CdAudioPlayback playbackEndStatus = playbackEndDrive.GetAudioStatus();

                playbackEndStatus.CurrentLba.Should().Be(1,
                    "the CD audio callback should advance only by the sectors that remain in the requested playback range");
                playbackEndStatus.Status.Should().Be(CdAudioStatus.Stopped,
                    "the CD audio callback should stop once the requested playback range has been fully consumed");
            }
        }

        // Arrange/Act/Assert (short read)
        using (TempFile shortReadTempFile = new("cdrom-parity-audio-short-read")) {
            byte[] shortDiscBytes = new byte[2352 + 1176];
            for (int i = 0; i < shortDiscBytes.Length; i++) {
                shortDiscBytes[i] = (byte)(i & 0xFF);
            }
            CdRomDrive shortReadDrive = CdRomTestFixture.CreateAudioCueBinDrive(
                shortReadTempFile,
                shortDiscBytes,
                out ICdRomImage shortReadImage,
                out Action<int> shortReadCallback,
                out _);
            using (shortReadImage) {
                shortReadDrive.PlayAudio(0, 2);
                shortReadCallback(588 * 2);
                CdAudioPlayback shortReadStatus = shortReadDrive.GetAudioStatus();

                shortReadStatus.CurrentLba.Should().Be(1,
                    "the CD audio callback should advance only across fully readable raw audio sectors");
                shortReadStatus.Status.Should().Be(CdAudioStatus.Stopped,
                    "the CD audio callback should stop when the drive can only provide a truncated trailing sector");
            }
        }
    }
}