namespace Spice86.Tests.CdRom;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.CdRom;
using Spice86.Core.Emulator.Devices.CdRom.Image;
using Spice86.Core.Emulator.InterruptHandlers.Mscdex;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System;
using System.Collections.Generic;

using Xunit;

/// <summary>Tests for <see cref="Mscdex.Dispatch"/> AL=0x10 (SendDeviceDriverRequest) path.</summary>
public sealed class MscdexDeviceDriverRequestTests {
    // Request packet field offsets within the DOS device driver header
    private const uint SubunitOffset = 1;
    private const uint CommandOffset = 2;
    private const uint StatusOffset = 3;
    // Transfer buffer pointer: offset word at 0x0E (14), segment word at 0x10 (16)
    // Matches DOSBox Staging: buffer = PhysicalMake(readw(hdr+0x10), readw(hdr+0x0E))
    private const uint DataOffset = 14;

    // Command codes
    private const byte CommandIoctlInput = 0x03;
    private const byte CommandPlayAudio = 0x84;
    private const byte CommandStopAudio = 0x85;
    private const byte CommandResumeAudio = 0x88;

    // IOCTL control codes — matching DOSBox Staging dos_mscdex.cpp MSCDEX_IOCTL_Input()
    private const byte IoctlMediaChanged = 0x09;
    private const byte IoctlAudioStatus = 0x0F;

    // Response status
    private const ushort StatusDone = 0x0100;

    // ES:BX location for request packet
    private const ushort RequestSegment = 0x1000;
    private const ushort RequestOffset = 0x0000;

    // IOCTL buffer location — in a separate segment to avoid overlap with the request packet bytes
    private const ushort IoctlSegment = 0x2000;
    private const ushort IoctlOffset = 0x0000;

    private sealed class FakeImage : ICdRomImage {
        public string? UpcEan => null;
        public string ImagePath => string.Empty;
        public IReadOnlyList<CdTrack> Tracks => new List<CdTrack>();
        public int TotalSectors => 100;
        public IsoVolumeDescriptor PrimaryVolume => new(string.Empty, 0, 0, 2048, 100);

        public int Read(int lba, Span<byte> destination, CdSectorMode mode) {
            destination.Clear();
            return destination.Length;
        }

        public void Dispose() { }
    }

    private sealed class TestContext {
        public Memory Memory { get; }
        public State State { get; }
        public Mscdex Mscdex { get; }
        public ICdRomDrive Drive { get; }
        public uint RequestBase { get; }

        public TestContext() {
            IMemoryDevice ram = new Ram(A20Gate.EndOfHighMemoryArea);
            AddressReadWriteBreakpoints breakpoints = new();
            A20Gate a20Gate = new(enabled: false);
            Memory = new Memory(breakpoints, ram, a20Gate, initializeResetVector: false);
            State = new State(CpuModel.INTEL_80386);

            ILoggerService logger = Substitute.For<ILoggerService>();
            Mscdex = new Mscdex(State, Memory, logger);

            Drive = new CdRomDrive(new FakeImage());

            MscdexDriveEntry entry = new('D', driveIndex: 3, Drive);
            Mscdex.AddDrive(entry);

            State.ES = RequestSegment;
            State.BX = RequestOffset;
            RequestBase = MemoryUtils.ToPhysicalAddress(RequestSegment, RequestOffset);

            // Set subunit 0 (first drive in list)
            Memory.UInt8[RequestBase + SubunitOffset] = 0;
        }

        public void SetCommand(byte command) {
            Memory.UInt8[RequestBase + CommandOffset] = command;
        }

        public ushort ReadStatus() {
            return Memory.UInt16[RequestBase + StatusOffset];
        }

        public void WriteIoctlBufferPointer() {
            Memory.UInt16[RequestBase + DataOffset] = IoctlOffset;
            Memory.UInt16[RequestBase + DataOffset + 2] = IoctlSegment;
        }

        public uint IoctlBase => MemoryUtils.ToPhysicalAddress(IoctlSegment, IoctlOffset);
    }

    [Fact]
    public void PlayAudio_WritesStartLbaAndSectorCount_AndSetsStatusDone() {
        // Arrange
        TestContext ctx = new();
        ctx.SetCommand(CommandPlayAudio);
        ctx.Memory.UInt32[ctx.RequestBase + DataOffset] = 10;      // start LBA
        ctx.Memory.UInt32[ctx.RequestBase + DataOffset + 4] = 50;  // sector count
        ctx.State.AL = 0x10;

        // Act
        ctx.Mscdex.Dispatch();

        // Assert
        ctx.ReadStatus().Should().Be(StatusDone);
        CdAudioPlayback status = ctx.Drive.GetAudioStatus();
        status.Status.Should().Be(CdAudioStatus.Playing);
        status.StartLba.Should().Be(10);
        status.EndLba.Should().Be(60);
    }

    [Fact]
    public void StopAudio_SetsStatusDone_AndStopsDrive() {
        // Arrange
        TestContext ctx = new();
        ctx.Drive.PlayAudio(0, 50);
        ctx.SetCommand(CommandStopAudio);
        ctx.State.AL = 0x10;

        // Act
        ctx.Mscdex.Dispatch();

        // Assert
        ctx.ReadStatus().Should().Be(StatusDone);
        ctx.Drive.GetAudioStatus().Status.Should().Be(CdAudioStatus.Stopped);
    }

    [Fact]
    public void ResumeAudio_FromPaused_SetsStatusDone_AndResumesDrive() {
        // Arrange
        TestContext ctx = new();
        ctx.Drive.PlayAudio(0, 50);
        ctx.Drive.PauseAudio();
        ctx.SetCommand(CommandResumeAudio);
        ctx.State.AL = 0x10;

        // Act
        ctx.Mscdex.Dispatch();

        // Assert
        ctx.ReadStatus().Should().Be(StatusDone);
        ctx.Drive.GetAudioStatus().Status.Should().Be(CdAudioStatus.Playing);
    }

    [Fact]
    public void IoctlInput_MediaChanged_ReturnsFalse_WhenNoChange() {
        // Arrange
        TestContext ctx = new();
        // Clear the initial "media changed" flag that is set on drive creation
        ctx.Drive.MediaState.ReadAndClearMediaChanged();
        ctx.SetCommand(CommandIoctlInput);
        ctx.WriteIoctlBufferPointer();
        ctx.Memory.UInt8[ctx.IoctlBase] = IoctlMediaChanged;
        ctx.State.AL = 0x10;

        // Act
        ctx.Mscdex.Dispatch();

        // Assert
        ctx.ReadStatus().Should().Be(StatusDone);
        ctx.Memory.UInt8[ctx.IoctlBase + 1].Should().Be(0);
    }

    [Fact]
    public void IoctlInput_AudioStatus_ReflectsPlayingState() {
        // Arrange
        TestContext ctx = new();
        ctx.Drive.PlayAudio(5, 30);
        ctx.Drive.PauseAudio();
        ctx.SetCommand(CommandIoctlInput);
        ctx.WriteIoctlBufferPointer();
        ctx.Memory.UInt8[ctx.IoctlBase] = IoctlAudioStatus;
        ctx.State.AL = 0x10;

        // Act
        ctx.Mscdex.Dispatch();

        // Assert
        ctx.ReadStatus().Should().Be(StatusDone);
        // Paused flag at offset 1 (single byte, per DOSBox Staging MSCDEX_IOCTL_Input case 0x0F)
        ctx.Memory.UInt8[ctx.IoctlBase + 1].Should().Be(1);
        // Start MSF at offsets 3-5 (min/sec/fr): start LBA=5, Red Book: 5+150=155 frames → 0min 2sec 5fr
        ctx.Memory.UInt8[ctx.IoctlBase + 3].Should().Be(0, "start min = 0");
        ctx.Memory.UInt8[ctx.IoctlBase + 4].Should().Be(2, "start sec = 2");
        ctx.Memory.UInt8[ctx.IoctlBase + 5].Should().Be(5, "start fr  = 5");
        // End MSF at offsets 7-9 (min/sec/fr): end LBA=35, Red Book: 35+150=185 frames → 0min 2sec 35fr
        ctx.Memory.UInt8[ctx.IoctlBase + 7].Should().Be(0, "end min = 0");
        ctx.Memory.UInt8[ctx.IoctlBase + 8].Should().Be(2, "end sec = 2");
        ctx.Memory.UInt8[ctx.IoctlBase + 9].Should().Be(35, "end fr  = 35");
    }

    [Fact]
    public void SendDeviceDriverRequest_UnknownSubunit_SetsErrorStatus() {
        // Arrange
        TestContext ctx = new();
        ctx.Memory.UInt8[ctx.RequestBase + SubunitOffset] = 99;  // no such subunit
        ctx.SetCommand(CommandPlayAudio);
        ctx.State.AL = 0x10;

        // Act
        ctx.Mscdex.Dispatch();

        // Assert
        (ctx.ReadStatus() & 0x8000).Should().Be(0x8000, "error bit must be set");
    }

    // IOCTL control codes for MSF-format tests — matching DOSBox Staging MSCDEX_IOCTL_Input()
    private const byte IoctlAudioDiskInfo = 0x0A;
    private const byte IoctlAudioTrackInfo = 0x0B;
    private const byte IoctlAudioSubchannel = 0x0C;

    /// <summary>
    /// A fake image that exposes two tracks so that MSF-related IOCTL responses can be tested.
    /// Track 1: audio, startLba=0, length=75 (covers LBA 0–74 inclusive).
    /// Track 2: data,  startLba=75, length=150 (covers LBA 75–224 inclusive).
    /// Total sectors = 225; lead-out at LBA 225.
    /// Track boundaries are exclusive-end: FindTrack(74)→track1, FindTrack(75)→track2.
    /// </summary>
    private sealed class FakeImageWithTracks : ICdRomImage {
        private readonly List<CdTrack> _tracks;

        public FakeImageWithTracks() {
            _tracks = new List<CdTrack> {
                // Track 1: audio, LBA [0, 75)
                new CdTrack(1, startLba: 0,  lengthSectors: 75,  2352, CdSectorMode.AudioRaw2352,   isAudio: true,  0, 0, new NullDataSource(), 0),
                // Track 2: data,  LBA [75, 225)
                new CdTrack(2, startLba: 75, lengthSectors: 150, 2048, CdSectorMode.CookedData2048, isAudio: false, 0, 0, new NullDataSource(), 0),
            };
        }

        public string? UpcEan => null;
        public string ImagePath => string.Empty;
        public IReadOnlyList<CdTrack> Tracks => _tracks;
        public int TotalSectors => 225;
        public IsoVolumeDescriptor PrimaryVolume => new(string.Empty, 0, 0, 2048, 225);

        public int Read(int lba, Span<byte> destination, CdSectorMode mode) {
            destination.Clear();
            return destination.Length;
        }

        public void Dispose() { }
    }

    /// <summary>Minimal <see cref="IDataSource"/> stub for test tracks that never have their data read.</summary>
    private sealed class NullDataSource : IDataSource {
        public long LengthBytes => 0;
        public int Read(long byteOffset, Span<byte> buffer) { buffer.Clear(); return buffer.Length; }
    }

    private sealed class TestContextWithTracks {
        public Memory Memory { get; }
        public State State { get; }
        public Mscdex Mscdex { get; }
        public ICdRomDrive Drive { get; }
        public uint RequestBase { get; }

        public TestContextWithTracks() {
            IMemoryDevice ram = new Ram(A20Gate.EndOfHighMemoryArea);
            AddressReadWriteBreakpoints breakpoints = new();
            A20Gate a20Gate = new(enabled: false);
            Memory = new Memory(breakpoints, ram, a20Gate, initializeResetVector: false);
            State = new State(CpuModel.INTEL_80386);

            ILoggerService logger = Substitute.For<ILoggerService>();
            Mscdex = new Mscdex(State, Memory, logger);

            Drive = new CdRomDrive(new FakeImageWithTracks());
            MscdexDriveEntry entry = new('D', driveIndex: 3, Drive);
            Mscdex.AddDrive(entry);

            State.ES = RequestSegment;
            State.BX = RequestOffset;
            RequestBase = MemoryUtils.ToPhysicalAddress(RequestSegment, RequestOffset);
            Memory.UInt8[RequestBase + SubunitOffset] = 0;
        }

        public void SetCommand(byte command) {
            Memory.UInt8[RequestBase + CommandOffset] = command;
        }

        public void WriteIoctlBufferPointer() {
            Memory.UInt16[RequestBase + DataOffset] = IoctlOffset;
            Memory.UInt16[RequestBase + DataOffset + 2] = IoctlSegment;
        }

        public ushort ReadStatus() {
            return Memory.UInt16[RequestBase + StatusOffset];
        }

        public uint IoctlBase => MemoryUtils.ToPhysicalAddress(IoctlSegment, IoctlOffset);
    }

    /// <summary>
    /// IOCTL 0x0A (Get Audio Disk Info): lead-out must be written as a 3-byte Red Book MSF
    /// at buffer offsets 3-5, matching DOSBox Staging MSCDEX_IOCTL_Input case 0x0A which
    /// writes TMSF (fr, sec, min) rather than a 4-byte DWORD LBA.
    /// Lead-out at LBA 225 → Red Book MSF = 225+150 = 375 frames → (min=0, sec=5, fr=0).
    /// DOSBox byte order: [3]=fr=0, [4]=sec=5, [5]=min=0.
    /// </summary>
    [Fact]
    public void IoctlAudioDiskInfo_WritesLeadOutAsMsf_NotAsLba() {
        // Arrange
        TestContextWithTracks ctx = new();
        ctx.SetCommand(CommandIoctlInput);
        ctx.WriteIoctlBufferPointer();
        ctx.Memory.UInt8[ctx.IoctlBase] = IoctlAudioDiskInfo;
        ctx.State.AL = 0x10;

        // Act
        ctx.Mscdex.Dispatch();

        // Assert
        ctx.ReadStatus().Should().Be(StatusDone);
        ctx.Memory.UInt8[ctx.IoctlBase + 1].Should().Be(1, "first track = 1");
        ctx.Memory.UInt8[ctx.IoctlBase + 2].Should().Be(2, "last track = 2");
        // Lead-out at LBA 225: 225+150=375 frames; fr=0, sec=5, min=0
        // DOSBox writes: [3]=fr, [4]=sec, [5]=min (frame at lowest address)
        ctx.Memory.UInt8[ctx.IoctlBase + 3].Should().Be(0,  "lead-out MSF frame   = 0");
        ctx.Memory.UInt8[ctx.IoctlBase + 4].Should().Be(5,  "lead-out MSF seconds = 5");
        ctx.Memory.UInt8[ctx.IoctlBase + 5].Should().Be(0,  "lead-out MSF minutes = 0");
    }

    /// <summary>
    /// IOCTL 0x0B (Get Audio Track Info): track start must be written as a 3-byte Red Book MSF
    /// at buffer offsets 2-4 and the attribute at offset 6, matching DOSBox Staging
    /// MSCDEX_IOCTL_Input case 0x0B which writes (fr, sec, min, 0x00, attr) — that is,
    /// frame at lowest address, 0x00 padding at offset 5, attribute at offset 6.
    /// Track 1 starts at LBA 0 → Red Book MSF = 0+150 = 150 frames → (fr=0, sec=2, min=0).
    /// </summary>
    [Fact]
    public void IoctlAudioTrackInfo_WritesStartAsMsfAndAttributeAtOffset6() {
        // Arrange
        TestContextWithTracks ctx = new();
        ctx.SetCommand(CommandIoctlInput);
        ctx.WriteIoctlBufferPointer();
        ctx.Memory.UInt8[ctx.IoctlBase] = IoctlAudioTrackInfo;
        ctx.Memory.UInt8[ctx.IoctlBase + 1] = 1; // query track 1
        ctx.State.AL = 0x10;

        // Act
        ctx.Mscdex.Dispatch();

        // Assert
        ctx.ReadStatus().Should().Be(StatusDone);
        // Track 1 start LBA = 0; Red Book MSF: 0+150=150; fr=0, sec=2, min=0
        // DOSBox writes: [2]=fr, [3]=sec, [4]=min, [5]=0x00 (padding), [6]=attr
        ctx.Memory.UInt8[ctx.IoctlBase + 2].Should().Be(0, "track start MSF frame   = 0");
        ctx.Memory.UInt8[ctx.IoctlBase + 3].Should().Be(2, "track start MSF seconds = 2");
        ctx.Memory.UInt8[ctx.IoctlBase + 4].Should().Be(0, "track start MSF minutes = 0");
        ctx.Memory.UInt8[ctx.IoctlBase + 5].Should().Be(0, "padding byte = 0x00");
        ctx.Memory.UInt8[ctx.IoctlBase + 6].Should().Be(0, "attribute = 0 for audio track");
    }

    /// <summary>
    /// IOCTL 0x0C (Get Audio Subchannel): writes attribute, BCD track number, index, relative MSF,
    /// and absolute MSF, matching DOSBox Staging MSCDEX_IOCTL_Input case 0x0C which calls GetAudioSub.
    /// Playing at LBA 80 (inside track 2 which starts at LBA 75):
    ///   absolute MSF = 80+150 = 230 frames → (fr=5, sec=3, min=0) → bytes [8]=min=0, [9]=sec=3, [10]=fr=5
    ///   relative MSF = 80-75 = 5 LBA → (fr=5, sec=0, min=0) → bytes [4]=min=0, [5]=sec=0, [6]=fr=5
    ///   [7] = 0x00 padding between relative and absolute MSF
    /// </summary>
    [Fact]
    public void IoctlAudioSubchannel_WritesProperMsfTrackAndIndex() {
        // Arrange
        TestContextWithTracks ctx = new();
        ctx.Drive.PlayAudio(80, 10); // playing at LBA 80, inside track 2 (starts at 75)
        ctx.SetCommand(CommandIoctlInput);
        ctx.WriteIoctlBufferPointer();
        ctx.Memory.UInt8[ctx.IoctlBase] = IoctlAudioSubchannel;
        ctx.State.AL = 0x10;

        // Act
        ctx.Mscdex.Dispatch();

        // Assert
        ctx.ReadStatus().Should().Be(StatusDone);
        // attribute = 4 for data track (track 2 control = 4)
        ctx.Memory.UInt8[ctx.IoctlBase + 1].Should().Be(4, "attribute = 4 for data track");
        // track number = 2 in BCD (single digit: same as decimal)
        ctx.Memory.UInt8[ctx.IoctlBase + 2].Should().Be(2, "BCD track number = 2");
        // index = 1
        ctx.Memory.UInt8[ctx.IoctlBase + 3].Should().Be(1, "index = 1");
        // relative MSF: LBA 80 - track2 start 75 = 5 → (min=0, sec=0, fr=5)
        ctx.Memory.UInt8[ctx.IoctlBase + 4].Should().Be(0, "relative min = 0");
        ctx.Memory.UInt8[ctx.IoctlBase + 5].Should().Be(0, "relative sec = 0");
        ctx.Memory.UInt8[ctx.IoctlBase + 6].Should().Be(5, "relative fr  = 5");
        // 0x00 padding at offset 7 (DOSBox Staging layout between rel and abs MSF)
        ctx.Memory.UInt8[ctx.IoctlBase + 7].Should().Be(0, "padding = 0x00");
        // absolute MSF: LBA 80+150=230 frames → fr=230%75=5, sec=3, min=0 → [8]=0, [9]=3, [10]=5
        ctx.Memory.UInt8[ctx.IoctlBase + 8].Should().Be(0, "absolute min = 0");
        ctx.Memory.UInt8[ctx.IoctlBase + 9].Should().Be(3, "absolute sec = 3");
        ctx.Memory.UInt8[ctx.IoctlBase + 10].Should().Be(5, "absolute fr  = 5");
    }

    // IOCTL Input 0x06 (Get Device Status): per DOSBox Staging dos_mscdex.cpp
    // CMscdex::GetDeviceStatus, the 32-bit status word at buffer+1 always has
    // capability bits 2/4/8/9 set, with audio-playing at bit 10 and drive-empty at bit 11.
    private const byte IoctlDeviceStatus = 0x06;
    private const uint DeviceStatusDoorOpen      = 1u << 0;
    private const uint DeviceStatusDoorLocked    = 1u << 1;
    private const uint DeviceStatusRawAndCooked  = 1u << 2;
    private const uint DeviceStatusCanReadAudio  = 1u << 4;
    private const uint DeviceStatusCanCtrlAudio  = 1u << 8;
    private const uint DeviceStatusRedbookHsg    = 1u << 9;
    private const uint DeviceStatusAudioPlaying  = 1u << 10;
    private const uint DeviceStatusDriveEmpty    = 1u << 11;

    [Fact]
    public void IoctlDeviceStatus_AlwaysReportsCapabilityBits() {
        // Arrange
        TestContext ctx = new();
        ctx.SetCommand(CommandIoctlInput);
        ctx.WriteIoctlBufferPointer();
        ctx.Memory.UInt8[ctx.IoctlBase] = IoctlDeviceStatus;
        ctx.State.AL = 0x10;

        // Act
        ctx.Mscdex.Dispatch();

        // Assert
        ctx.ReadStatus().Should().Be(StatusDone);
        uint status = ctx.Memory.UInt32[ctx.IoctlBase + 1];
        (status & DeviceStatusRawAndCooked).Should().Be(DeviceStatusRawAndCooked, "bit 2 (raw+cooked) is always set");
        (status & DeviceStatusCanReadAudio).Should().Be(DeviceStatusCanReadAudio, "bit 4 (can read audio) is always set");
        (status & DeviceStatusCanCtrlAudio).Should().Be(DeviceStatusCanCtrlAudio, "bit 8 (can control audio) is always set");
        (status & DeviceStatusRedbookHsg).Should().Be(DeviceStatusRedbookHsg, "bit 9 (Red Book + HSG) is always set");
    }

    [Fact]
    public void IoctlDeviceStatus_AudioPlayingBit_IsAtBit10_NotBit9() {
        // Arrange
        TestContext ctx = new();
        ctx.Drive.PlayAudio(0, 50);
        ctx.SetCommand(CommandIoctlInput);
        ctx.WriteIoctlBufferPointer();
        ctx.Memory.UInt8[ctx.IoctlBase] = IoctlDeviceStatus;
        ctx.State.AL = 0x10;

        // Act
        ctx.Mscdex.Dispatch();

        // Assert
        ctx.ReadStatus().Should().Be(StatusDone);
        uint status = ctx.Memory.UInt32[ctx.IoctlBase + 1];
        (status & DeviceStatusAudioPlaying).Should().Be(DeviceStatusAudioPlaying, "bit 10 (audio playing) must be set when audio is playing");
    }

    [Fact]
    public void IoctlDeviceStatus_DriveEmptyBit_SetWhenDoorIsOpen() {
        // Arrange
        TestContext ctx = new();
        ctx.Drive.MediaState.IsDoorOpen = true;
        ctx.SetCommand(CommandIoctlInput);
        ctx.WriteIoctlBufferPointer();
        ctx.Memory.UInt8[ctx.IoctlBase] = IoctlDeviceStatus;
        ctx.State.AL = 0x10;

        // Act
        ctx.Mscdex.Dispatch();

        // Assert
        ctx.ReadStatus().Should().Be(StatusDone);
        uint status = ctx.Memory.UInt32[ctx.IoctlBase + 1];
        (status & DeviceStatusDoorOpen).Should().Be(DeviceStatusDoorOpen, "bit 0 (door open)");
        (status & DeviceStatusDriveEmpty).Should().Be(DeviceStatusDriveEmpty, "bit 11 (drive empty) must be set when no media is loaded (i.e. door open)");
    }

    // IOCTL Input 0x01 (Get Current Position): per DOSBox Staging, addr_mode != 0 and != 1
    // must return invalid-function (0x03) error in the request status word.
    private const byte IoctlCurrentPosition = 0x01;
    private const ushort StatusErrorBit = 0x8000;
    private const ushort MscdexInvalidFunctionError = 0x03;

    [Fact]
    public void IoctlCurrentPosition_InvalidAddressMode_ReturnsInvalidFunctionError() {
        // Arrange
        TestContext ctx = new();
        ctx.SetCommand(CommandIoctlInput);
        ctx.WriteIoctlBufferPointer();
        ctx.Memory.UInt8[ctx.IoctlBase] = IoctlCurrentPosition;
        ctx.Memory.UInt8[ctx.IoctlBase + 1] = 0x77;  // unknown addr_mode (not 0 = HSG, not 1 = Red Book)
        ctx.State.AL = 0x10;

        // Act
        ctx.Mscdex.Dispatch();

        // Assert
        ushort status = ctx.ReadStatus();
        (status & StatusErrorBit).Should().Be(StatusErrorBit, "error bit must be set for invalid address mode");
        (status & 0xFF).Should().Be(MscdexInvalidFunctionError, "status low byte must carry MSCDEX invalid-function error code");
    }

    // IOCTL Input 0x07 (Get Sector Size): per DOSBox Staging, mode byte != 0 and != 1
    // must return invalid-function (0x03) error in the request status word.
    private const byte IoctlSectorSize = 0x07;

    [Fact]
    public void IoctlSectorSize_InvalidModeByte_ReturnsInvalidFunctionError() {
        // Arrange
        TestContext ctx = new();
        ctx.SetCommand(CommandIoctlInput);
        ctx.WriteIoctlBufferPointer();
        ctx.Memory.UInt8[ctx.IoctlBase] = IoctlSectorSize;
        ctx.Memory.UInt8[ctx.IoctlBase + 1] = 0x99;  // unknown mode (not 0 = cooked, not 1 = raw)
        ctx.State.AL = 0x10;

        // Act
        ctx.Mscdex.Dispatch();

        // Assert
        ushort status = ctx.ReadStatus();
        (status & StatusErrorBit).Should().Be(StatusErrorBit, "error bit must be set for invalid sector size mode");
        (status & 0xFF).Should().Be(MscdexInvalidFunctionError, "status low byte must carry MSCDEX invalid-function error code");
    }
}
