namespace Spice86.Tests.Dos;

using System;
using System.Collections.Generic;
using System.IO;

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
        using VirtualIsoImage image = CreateVirtualIsoImage(tempFile);
        byte[] buffer = new byte[2352];

        // Act
        int bytesRead = image.Read(16, buffer, CdSectorMode.Raw2352);

        // Assert
        bytesRead.Should().Be(0,
            "DOSBox rejects raw 2352-byte reads from cooked-only virtual ISO tracks");
    }

    [Fact]
    public void IsoImage_Read_RawModeOnCookedTrack_ReturnsZero() {
        // Arrange
        using TempFile tempFile = new("cdrom-parity");
        string isoPath = CreateIsoFile(tempFile);
        using IsoImage image = new(isoPath);
        byte[] buffer = new byte[2352];

        // Act
        int bytesRead = image.Read(16, buffer, CdSectorMode.Raw2352);

        // Assert
        bytesRead.Should().Be(0,
            "DOSBox rejects raw 2352-byte reads from plain ISO images because they only expose cooked 2048-byte sectors");
    }

    [Fact]
    public void CdRomDrive_Eject_DoesNotOpenDoorOrFlagMediaChangedForMountedImages() {
        // Arrange
        using TempFile tempFile = new("cdrom-parity");
        using VirtualIsoImage image = CreateVirtualIsoImage(tempFile);
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
            "DOSBox image-backed drives do not transition to an open tray state on eject requests");
        postEjectMediaChanged.Should().BeFalse(
            "DOSBox image-backed drives treat eject as a no-op rather than a media change");
    }

    [Fact]
    public void CdRomDrive_AudioCallback_StopsAtPlaybackEndAndIgnoresIncompleteTrailingSectorReads() {
        // Arrange
        using ScriptedCdRomImage playbackEndImage = new(2352, 2352);
        CdRomDrive playbackEndDrive = CreateAudioTestDrive(playbackEndImage, out Action<int> playbackEndCallback,
            out SoundChannel playbackEndChannel);
        playbackEndDrive.PlayAudio(0, 1);

        using ScriptedCdRomImage shortReadImage = new(2352, 1176);
        CdRomDrive shortReadDrive = CreateAudioTestDrive(shortReadImage, out Action<int> shortReadCallback,
            out SoundChannel shortReadChannel);
        shortReadDrive.PlayAudio(0, 2);

        // Act
        playbackEndCallback(588 * 2);
        CdAudioPlayback playbackEndStatus = playbackEndDrive.GetAudioStatus();

        shortReadCallback(588 * 2);
        CdAudioPlayback shortReadStatus = shortReadDrive.GetAudioStatus();

        // Assert
        playbackEndStatus.CurrentLba.Should().Be(1,
            "the CD audio callback should advance only by the sectors that remain in the requested playback range");
        playbackEndStatus.Status.Should().Be(CdAudioStatus.Stopped,
            "the CD audio callback should stop once the requested playback range has been fully consumed");
        playbackEndImage.ReadCallCount.Should().Be(1,
            "the CD audio callback should not read sectors beyond the requested end LBA");

        shortReadStatus.CurrentLba.Should().Be(1,
            "the CD audio callback should advance only across fully readable raw audio sectors");
        shortReadStatus.Status.Should().Be(CdAudioStatus.Stopped,
            "the CD audio callback should stop when the drive can only provide a truncated trailing sector");
        shortReadImage.ReadCallCount.Should().Be(2,
            "the CD audio callback should stop after the first truncated sector instead of attempting to advance further");
    }

    private static VirtualIsoImage CreateVirtualIsoImage(TempFile tempFile) {
        string sourceDirectory = tempFile.CreateDirectory("source");
        File.WriteAllText(Path.Join(sourceDirectory, "README.TXT"), "Spice86");
        return new VirtualIsoImage(sourceDirectory, "SPICE86");
    }

    private static string CreateIsoFile(TempFile tempFile) {
        using VirtualIsoImage virtualIsoImage = CreateVirtualIsoImage(tempFile);
        byte[] isoBytes = new byte[virtualIsoImage.TotalSectors * 2048];
        for (int lba = 0; lba < virtualIsoImage.TotalSectors; lba++) {
            int offset = lba * 2048;
            int bytesRead = virtualIsoImage.Read(lba, isoBytes.AsSpan(offset, 2048), CdSectorMode.CookedData2048);
            if (bytesRead != 2048) {
                throw new InvalidOperationException($"Expected to materialize a full cooked sector at LBA {lba}.");
            }
        }

        return tempFile.CreateFile("disc.iso", isoBytes);
    }

    private static CdRomDrive CreateAudioTestDrive(ICdRomImage image, out Action<int> audioCallback,
        out SoundChannel channel) {
        ISoundChannelCreator channelCreator = Substitute.For<ISoundChannelCreator>();
        Action<int>? capturedAudioCallback = null;
        SoundChannel? capturedChannel = null;
        channelCreator
            .AddChannel(Arg.Any<Action<int>>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<HashSet<ChannelFeature>>())
            .Returns(callInfo => {
                Action<int> handler = (Action<int>)callInfo[0];
                SoundChannel createdChannel = new SoundChannel(handler, (string)callInfo[2],
                    (HashSet<ChannelFeature>)callInfo[3]);
                capturedAudioCallback = handler;
                capturedChannel = createdChannel;
                return createdChannel;
            });
        IDriveActivityNotifier activityNotifier = Substitute.For<IDriveActivityNotifier>();
        CdRomDrive drive = new(image, channelCreator, activityNotifier, driveLetter: 'D');
        if (capturedAudioCallback == null || capturedChannel == null) {
            throw new InvalidOperationException("CD audio test drive failed to register its mixer callback.");
        }

        audioCallback = capturedAudioCallback;
        channel = capturedChannel;
        return drive;
    }

    private sealed class ScriptedCdRomImage : ICdRomImage {
        private readonly Queue<ReadResponse> _responses = new();

        public int ReadCallCount { get; private set; }

        public IReadOnlyList<CdTrack> Tracks { get; }

        public int TotalSectors { get; } = 64;

        public IsoVolumeDescriptor PrimaryVolume { get; } = new("TESTDISC", 0, 0, 2048, 64);

        public string? UpcEan => null;

        public string ImagePath { get; } = "disc.bin";

        public ScriptedCdRomImage(params int[] readByteCounts) {
            IDataSource source = Substitute.For<IDataSource>();
            Tracks = new[] {
                new CdTrack(1, 0, TotalSectors, 2352, CdSectorMode.AudioRaw2352, true, 150, 0, source, 0),
            };
            foreach (int readByteCount in readByteCounts) {
                _responses.Enqueue(new ReadResponse(readByteCount, 0x2A));
            }
        }

        public int Read(int lba, Span<byte> destination, CdSectorMode mode) {
            ReadCallCount++;
            if (_responses.Count == 0) {
                return 0;
            }

            ReadResponse response = _responses.Dequeue();
            int bytesToCopy = Math.Min(response.ByteCount, destination.Length);
            if (bytesToCopy > 0) {
                destination.Slice(0, bytesToCopy).Fill(response.FillValue);
            }
            return bytesToCopy;
        }

        public void Dispose() {
        }

        private readonly struct ReadResponse {
            public int ByteCount { get; }

            public byte FillValue { get; }

            public ReadResponse(int byteCount, byte fillValue) {
                ByteCount = byteCount;
                FillValue = fillValue;
            }
        }
    }
}