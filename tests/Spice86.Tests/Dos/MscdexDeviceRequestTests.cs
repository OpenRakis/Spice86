namespace Spice86.Tests.Dos;

using System;
using System.Collections.Generic;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.CdRom;
using Spice86.Core.Emulator.InterruptHandlers.Mscdex;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Shared.Emulator.Storage.CdRom;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using Xunit;

public class MscdexDeviceRequestTests {
    private const byte CommandIoctlInput = 0x03;
    private const byte CommandSeek = 0x83;
    private const byte CommandPlayAudio = 0x84;
    private const byte CommandStopAudio = 0x85;
    private const byte CommandResumeAudio = 0x88;

    private const byte IoctlCurrentPosition = 0x01;
    private const byte IoctlMediaChanged = 0x09;
    private const byte IoctlAudioStatus = 0x0F;

    private const uint RequestSubunitOffset = 1;
    private const uint RequestCommandOffset = 2;
    private const uint RequestStatusOffset = 3;
    private const uint RequestAddressingModeOffset = 13;
    private const uint IoctlBufferPtrOffset = 14;
    private const uint PlayAudioStartOffset = 14;
    private const uint PlayAudioLengthOffset = 18;
    private const uint SeekSectorOffset = 20;

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

    private sealed class MscdexTestHarness {
        private const ushort RequestSegment = 0x2000;
        private const ushort BufferSegment = 0x2100;

        public State State { get; }
        public Memory Memory { get; }
        public TestCdRomDrive Drive { get; }
        public Mscdex Mscdex { get; }
        public uint RequestBaseAddress { get; }
        public uint BufferBaseAddress { get; }
        public ushort RequestStatusWord => Memory.UInt16[RequestBaseAddress + RequestStatusOffset];

        public MscdexTestHarness() {
            State = new State(CpuModel.INTEL_80286);
            Memory = new Memory(new(), new Ram(0x200000), new A20Gate(), new RealModeMmu386(), false);
            Drive = new TestCdRomDrive();
            ILoggerService loggerService = Substitute.For<ILoggerService>();
            Mscdex = new Mscdex(State, Memory, loggerService);
            Mscdex.AddDrive(new MscdexDriveEntry('D', 3, Drive));
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
            return 0;
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