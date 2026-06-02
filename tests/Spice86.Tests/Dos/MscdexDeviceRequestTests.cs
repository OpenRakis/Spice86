namespace Spice86.Tests.Dos;

using System;
using System.Collections.Generic;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.CdRom;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.InterruptHandlers.Mscdex;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Shared.Emulator.Storage.CdRom;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using Xunit;

public class MscdexDeviceRequestTests {
    private const byte CommandIoctlInput = 0x03;
    private const byte CommandIoctlOutput = 0x0C;
    private const byte CommandReadLong = 0x80;
    private const byte CommandSeek = 0x83;
    private const byte CommandPlayAudio = 0x84;
    private const byte CommandStopAudio = 0x85;
    private const byte CommandResumeAudio = 0x88;

    private const byte IoctlCurrentPosition = 0x01;
    private const byte IoctlChannelControl = 0x04;
    private const byte IoctlMediaChanged = 0x09;
    private const byte IoctlAudioStatus = 0x0F;
    private const byte IoctlOutputChannelControl = 0x03;

    private const uint RequestSubunitOffset = 1;
    private const uint RequestCommandOffset = 2;
    private const uint RequestStatusOffset = 3;
    private const uint RequestAddressingModeOffset = 13;
    private const uint IoctlBufferPtrOffset = 14;
    private const uint PlayAudioStartOffset = 14;
    private const uint PlayAudioLengthOffset = 18;
    private const uint ReadLongSectorCountOffset = 18;
    private const uint SeekSectorOffset = 20;
    private const uint ReadLongStartSectorOffset = 20;
    private const uint ReadLongRawFlagOffset = 24;

    [Fact]
    public void SendDeviceDriverRequest_IoctlMediaChanged_UsesDosBoxSwapRequestCodes() {
        // Arrange
        MscdexTestHarness harness = new();

        // Act
        harness.DispatchIoctlInput(IoctlMediaChanged);
        byte firstStatus = harness.Memory.UInt8[harness.BufferBaseAddress + 1];

        harness.DispatchIoctlInput(IoctlMediaChanged);
        byte secondStatus = harness.Memory.UInt8[harness.BufferBaseAddress + 1];

        // Assert
        firstStatus.Should().Be(0xFF,
            "MSCDEX IOCTL media-change should report swap requested on the first query after media insertion");
        secondStatus.Should().Be(0x01,
            "MSCDEX IOCTL media-change should report 0x01 once the pending swap notification has been consumed");
    }

    [Fact]
    public void SendDeviceDriverRequest_Seek_UpdatesCurrentPositionWhenPlaybackIsIdle() {
        // Arrange
        MscdexTestHarness harness = new();

        // Act
        harness.DispatchSeek(321);
        harness.DispatchIoctlInput(IoctlCurrentPosition, 0);
        uint currentLba = harness.Memory.UInt32[harness.BufferBaseAddress + 2];

        // Assert
        currentLba.Should().Be(321u,
            "MSCDEX seek should update the idle current-position state used by IOCTL current-position queries");
    }

    [Fact]
    public void SendDeviceDriverRequest_StopAndResume_PreservePausedAudioSessionAndDosBoxAudioStatus() {
        // Arrange
        MscdexTestHarness harness = new();

        // Act
        harness.DispatchPlayAudio(100, 75);
        harness.Drive.AdvancePlaybackTo(120);
        harness.DispatchStopAudio();
        harness.DispatchIoctlInput(IoctlAudioStatus);

        byte pauseFlag = harness.Memory.UInt8[harness.BufferBaseAddress + 1];
        byte startMinute = harness.Memory.UInt8[harness.BufferBaseAddress + 3];
        byte startSecond = harness.Memory.UInt8[harness.BufferBaseAddress + 4];
        byte startFrame = harness.Memory.UInt8[harness.BufferBaseAddress + 5];
        byte endMinute = harness.Memory.UInt8[harness.BufferBaseAddress + 7];
        byte endSecond = harness.Memory.UInt8[harness.BufferBaseAddress + 8];
        byte endFrame = harness.Memory.UInt8[harness.BufferBaseAddress + 9];

        harness.DispatchResumeAudio();
        CdAudioPlayback resumedPlayback = harness.Drive.GetAudioStatus();

        // Assert
        pauseFlag.Should().Be(1,
            "MSCDEX stop should preserve a resumable paused audio session instead of discarding playback state");
        startMinute.Should().Be(0);
        startSecond.Should().Be(3);
        startFrame.Should().Be(45,
            "MSCDEX audio status should report the paused playback position in Red Book MSF");
        endMinute.Should().Be(0);
        endSecond.Should().Be(3);
        endFrame.Should().Be(0,
            "MSCDEX audio status should report the requested playback length, not an absolute end LBA");
        resumedPlayback.Status.Should().Be(CdAudioStatus.Playing,
            "MSCDEX resume should restart a previously paused audio session");
    }

    [Fact]
    public void SendDeviceDriverRequest_PlayAndResume_SetAudioPlayingBitInStatusWord() {
        // Arrange
        MscdexTestHarness harness = new();

        // Act
        harness.DispatchPlayAudio(100, 75);
        ushort playStatus = harness.RequestStatusWord;

        harness.Drive.AdvancePlaybackTo(120);
        harness.DispatchStopAudio();
        ushort stopStatus = harness.RequestStatusWord;

        harness.DispatchResumeAudio();
        ushort resumeStatus = harness.RequestStatusWord;

        // Assert
        playStatus.Should().Be(0x0300,
            "MSCDEX request status should set the audio-playing bit while playback is active");
        stopStatus.Should().Be(0x0100,
            "MSCDEX stop should clear the audio-playing bit once playback has been paused");
        resumeStatus.Should().Be(0x0300,
            "MSCDEX resume should restore the audio-playing bit when playback becomes active again");
    }

    [Fact]
    public void SendDeviceDriverRequest_IoctlChannelControl_ClampsDosBoxRoutingAndAppliesLiveMixerState() {
        // Arrange
        ICdRomImage image = Substitute.For<ICdRomImage>();
        image.Tracks.Returns(Array.Empty<CdTrack>());

        ISoundChannelCreator channelCreator = Substitute.For<ISoundChannelCreator>();
        SoundChannel? channel = null;
        channelCreator
            .AddChannel(Arg.Any<Action<int>>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<HashSet<ChannelFeature>>())
            .Returns(callInfo => {
                Action<int> handler = (Action<int>)callInfo[0];
                SoundChannel createdChannel = new SoundChannel(handler, (string)callInfo[2], (HashSet<ChannelFeature>)callInfo[3]);
                channel = createdChannel;
                return createdChannel;
            });
        IDriveActivityNotifier activityNotifier = Substitute.For<IDriveActivityNotifier>();
        CdRomDrive drive = new(image, channelCreator, activityNotifier, 'D');
        MscdexTestHarness harness = new(drive);
        if (channel == null) {
            throw new InvalidOperationException("CD audio channel was not registered.");
        }

        // Act
        harness.DispatchIoctlOutputChannelControl(3, 64, 9, 192, 2, 111, 3, 222);
        harness.DispatchIoctlInput(IoctlChannelControl);

        // Assert
        channel.AppVolume.Left.Should().BeApproximately(64.0f / 255.0f, 0.0001f,
            "MSCDEX IOCTL channel-control should update the live left application gain on the CD audio mixer channel");
        channel.AppVolume.Right.Should().BeApproximately(192.0f / 255.0f, 0.0001f,
            "MSCDEX IOCTL channel-control should update the live right application gain on the CD audio mixer channel");
        channel.ChannelMap.Should().Be(StereoLine.StereoMap,
            "DOSBox clamps invalid channel-control output routes back to left/right before applying them");
        harness.Memory.UInt8[harness.BufferBaseAddress + 1].Should().Be(0,
            "invalid left output routes should round-trip as the DOSBox left default");
        harness.Memory.UInt8[harness.BufferBaseAddress + 2].Should().Be(64);
        harness.Memory.UInt8[harness.BufferBaseAddress + 3].Should().Be(1,
            "invalid right output routes should round-trip as the DOSBox right default");
        harness.Memory.UInt8[harness.BufferBaseAddress + 4].Should().Be(192);
        harness.Memory.UInt8[harness.BufferBaseAddress + 5].Should().Be(2);
        harness.Memory.UInt8[harness.BufferBaseAddress + 6].Should().Be(111);
        harness.Memory.UInt8[harness.BufferBaseAddress + 7].Should().Be(3);
        harness.Memory.UInt8[harness.BufferBaseAddress + 8].Should().Be(222);
    }

    [Fact]
    public void SendDeviceDriverRequest_ReadLong_FailsWhenDriveReturnsShortRawSector() {
        // Arrange
        TestCdRomDrive drive = new TestCdRomDrive {
            ReadByteCount = 2048,
            ReadFillValue = 0x7A,
        };
        MscdexTestHarness harness = new(drive);
        harness.Memory.UInt8[harness.BufferBaseAddress] = 0x5A;

        // Act
        harness.DispatchReadLong(16, 1, rawFlag: 1);

        // Assert
        harness.RequestStatusWord.Should().Be(0x8000,
            "MSCDEX Read Long should fail when the drive cannot supply a full raw 2352-byte sector");
        harness.Memory.UInt8[harness.BufferBaseAddress].Should().Be(0x5A,
            "failed Read Long requests should not overwrite the caller buffer with truncated sector data");
    }

    private sealed class MscdexTestHarness {
        private const ushort RequestSegment = 0x2000;
        private const ushort BufferSegment = 0x2100;

        public State State { get; }
        public Memory Memory { get; }
        private readonly TestCdRomDrive? _testDrive;

        public TestCdRomDrive Drive => _testDrive ?? throw new InvalidOperationException(
            "This harness instance was created with a real drive and does not expose the scripted test drive helpers.");

        public ICdRomDrive RegisteredDrive { get; }
        public Mscdex Mscdex { get; }
        public uint RequestBaseAddress { get; }
        public uint BufferBaseAddress { get; }
        public ushort RequestStatusWord => Memory.UInt16[RequestBaseAddress + RequestStatusOffset];

        public MscdexTestHarness()
            : this(new TestCdRomDrive()) {
        }

        public MscdexTestHarness(TestCdRomDrive drive)
            : this(drive, drive) {
        }

        public MscdexTestHarness(ICdRomDrive drive)
            : this(drive, drive as TestCdRomDrive) {
        }

        private MscdexTestHarness(ICdRomDrive drive, TestCdRomDrive? testDrive) {
            State = new State(CpuModel.INTEL_80286);
            Memory = new Memory(new(), new Ram(0x200000), new A20Gate(), new RealModeMmu386(), false);
            RegisteredDrive = drive;
            _testDrive = testDrive;
            ILoggerService loggerService = Substitute.For<ILoggerService>();
            Mscdex = new Mscdex(State, Memory, loggerService);
            Mscdex.AddDrive(new MscdexDriveEntry('D', 3, drive));
            RequestBaseAddress = MemoryUtils.ToPhysicalAddress(RequestSegment, 0);
            BufferBaseAddress = MemoryUtils.ToPhysicalAddress(BufferSegment, 0);
        }

        public void DispatchIoctlInput(byte controlCode) {
            DispatchIoctlInput(controlCode, 0);
        }

        public void DispatchIoctlInput(byte controlCode, byte addressMode) {
            InitialiseRequest(CommandIoctlInput);
            Memory.UInt16[RequestBaseAddress + IoctlBufferPtrOffset] = 0;
            Memory.UInt16[RequestBaseAddress + IoctlBufferPtrOffset + 2] = BufferSegment;
            Memory.UInt8[BufferBaseAddress] = controlCode;
            Memory.UInt8[BufferBaseAddress + 1] = addressMode;
            Dispatch();
        }

        public void DispatchSeek(uint lba) {
            InitialiseRequest(CommandSeek);
            Memory.UInt8[RequestBaseAddress + RequestAddressingModeOffset] = 0;
            Memory.UInt32[RequestBaseAddress + SeekSectorOffset] = lba;
            Dispatch();
        }

        public void DispatchPlayAudio(uint startLba, uint sectorCount) {
            InitialiseRequest(CommandPlayAudio);
            Memory.UInt8[RequestBaseAddress + RequestAddressingModeOffset] = 0;
            Memory.UInt32[RequestBaseAddress + PlayAudioStartOffset] = startLba;
            Memory.UInt32[RequestBaseAddress + PlayAudioLengthOffset] = sectorCount;
            Dispatch();
        }

        public void DispatchIoctlOutputChannelControl(byte output0, byte volume0, byte output1, byte volume1,
            byte output2, byte volume2, byte output3, byte volume3) {
            InitialiseRequest(CommandIoctlOutput);
            Memory.UInt16[RequestBaseAddress + IoctlBufferPtrOffset] = 0;
            Memory.UInt16[RequestBaseAddress + IoctlBufferPtrOffset + 2] = BufferSegment;
            Memory.UInt8[BufferBaseAddress] = IoctlOutputChannelControl;
            Memory.UInt8[BufferBaseAddress + 1] = output0;
            Memory.UInt8[BufferBaseAddress + 2] = volume0;
            Memory.UInt8[BufferBaseAddress + 3] = output1;
            Memory.UInt8[BufferBaseAddress + 4] = volume1;
            Memory.UInt8[BufferBaseAddress + 5] = output2;
            Memory.UInt8[BufferBaseAddress + 6] = volume2;
            Memory.UInt8[BufferBaseAddress + 7] = output3;
            Memory.UInt8[BufferBaseAddress + 8] = volume3;
            Dispatch();
        }

        public void DispatchReadLong(uint startLba, ushort sectorCount, byte rawFlag) {
            InitialiseRequest(CommandReadLong);
            Memory.UInt8[RequestBaseAddress + RequestAddressingModeOffset] = 0;
            Memory.UInt16[RequestBaseAddress + IoctlBufferPtrOffset] = 0;
            Memory.UInt16[RequestBaseAddress + IoctlBufferPtrOffset + 2] = BufferSegment;
            Memory.UInt16[RequestBaseAddress + ReadLongSectorCountOffset] = sectorCount;
            Memory.UInt32[RequestBaseAddress + ReadLongStartSectorOffset] = startLba;
            Memory.UInt8[RequestBaseAddress + ReadLongRawFlagOffset] = rawFlag;
            Dispatch();
        }

        public void DispatchStopAudio() {
            InitialiseRequest(CommandStopAudio);
            Dispatch();
        }

        public void DispatchResumeAudio() {
            InitialiseRequest(CommandResumeAudio);
            Dispatch();
        }

        private void InitialiseRequest(byte command) {
            State.AL = 0x10;
            State.ES = RequestSegment;
            State.BX = 0;
            Memory.UInt8[RequestBaseAddress + RequestSubunitOffset] = 0;
            Memory.UInt8[RequestBaseAddress + RequestCommandOffset] = command;
            Memory.UInt16[RequestBaseAddress + RequestStatusOffset] = 0;
        }

        private void Dispatch() {
            Mscdex.Dispatch();
        }
    }

    private sealed class TestCdRomDrive : ICdRomDrive {
        private readonly IReadOnlyList<TableOfContentsEntry> _tableOfContents = new[] {
            new TableOfContentsEntry(1, 0, true, 0, 1),
            new TableOfContentsEntry(0xAA, 1000, false, 4, 1),
        };

        private CdAudioPlayback? _playback;

        public int ReadByteCount { get; set; }

        public byte ReadFillValue { get; set; }

        public ICdRomImage Image { get; }

        public CdRomMediaState MediaState { get; } = new();

        public bool IsAudioPlaying {
            get {
                if (_playback == null) {
                    return false;
                }
                return _playback.Status == CdAudioStatus.Playing;
            }
        }

        public int ImageCount => 1;

        public IReadOnlyList<string> AllImagePaths { get; } = new[] { "disc.bin" };

        public TestCdRomDrive() {
            Image = Substitute.For<ICdRomImage>();
        }

        public void AdvancePlaybackTo(int currentLba) {
            if (_playback == null) {
                throw new InvalidOperationException("Playback must be active before advancing it.");
            }
            _playback.CurrentLba = currentLba;
        }

        public void AddImage(ICdRomImage image) {
        }

        public void SwapToNextDisc() {
        }

        public void SwapToIndex(int index) {
        }

        public int Read(int lba, int sectorCount, Span<byte> destination, CdSectorMode mode) {
            int bytesToCopy = Math.Min(ReadByteCount, destination.Length);
            if (bytesToCopy > 0) {
                destination.Slice(0, bytesToCopy).Fill(ReadFillValue);
            }
            return bytesToCopy;
        }

        public IReadOnlyList<TableOfContentsEntry> GetTableOfContents() {
            return _tableOfContents;
        }

        public TableOfContentsEntry? GetTrackInfo(int trackNumber) {
            foreach (TableOfContentsEntry entry in _tableOfContents) {
                if (entry.TrackNumber == trackNumber) {
                    return entry;
                }
            }
            return null;
        }

        public DiscInfo GetDiscInfo() {
            return new DiscInfo(1, 1, 1000, 1000);
        }

        public void PlayAudio(int startLba, int sectorCount) {
            _playback = new CdAudioPlayback(startLba, startLba + sectorCount);
            _playback.Status = CdAudioStatus.Playing;
        }

        public void StopAudio() {
            if (_playback != null) {
                _playback.Status = CdAudioStatus.Stopped;
            }
        }

        public void ResumeAudio() {
            if (_playback != null && _playback.Status == CdAudioStatus.Paused) {
                _playback.Status = CdAudioStatus.Playing;
            }
        }

        public void PauseAudio() {
            if (_playback != null && _playback.Status == CdAudioStatus.Playing) {
                _playback.Status = CdAudioStatus.Paused;
            }
        }

        public CdAudioPlayback GetAudioStatus() {
            if (_playback != null) {
                return _playback;
            }
            CdAudioPlayback stoppedPlayback = new CdAudioPlayback(0, 0);
            stoppedPlayback.Status = CdAudioStatus.Stopped;
            return stoppedPlayback;
        }

        public void ApplyChannelControl(byte leftOutput, byte leftVolume, byte rightOutput, byte rightVolume) {
        }

        public string? GetUpc() {
            return null;
        }

        public void Eject() {
            MediaState.IsDoorOpen = true;
            MediaState.NotifyMediaChanged();
        }

        public void Insert(ICdRomImage image) {
            MediaState.IsDoorOpen = false;
            MediaState.NotifyMediaChanged();
        }
    }
}