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

/// <summary>Tests for <see cref="MscdexService.Dispatch"/> AL=0x10 (SendDeviceDriverRequest) path.</summary>
public sealed class MscdexDeviceDriverRequestTests {
    // Request packet field offsets within the DOS device driver header
    private const uint SubunitOffset = 1;
    private const uint CommandOffset = 2;
    private const uint StatusOffset = 3;
    private const uint DataOffset = 13;

    // Command codes
    private const byte CommandIoctlInput = 0x03;
    private const byte CommandPlayAudio = 0x84;
    private const byte CommandStopAudio = 0x85;
    private const byte CommandResumeAudio = 0x88;

    // IOCTL control codes
    private const byte IoctlMediaChanged = 0x03;
    private const byte IoctlAudioStatus = 0x0E;

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
        public MscdexService Mscdex { get; }
        public ICdRomDrive Drive { get; }
        public uint RequestBase { get; }

        public TestContext() {
            IMemoryDevice ram = new Ram(A20Gate.EndOfHighMemoryArea);
            AddressReadWriteBreakpoints breakpoints = new();
            A20Gate a20Gate = new(enabled: false);
            Memory = new Memory(breakpoints, ram, a20Gate, initializeResetVector: false);
            State = new State(CpuModel.INTEL_80386);

            ILoggerService logger = Substitute.For<ILoggerService>();
            Mscdex = new MscdexService(State, Memory, logger);

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
        // Paused flag at offset 1
        ctx.Memory.UInt16[ctx.IoctlBase + 1].Should().Be(1);
        // Start LBA at offset 3
        ctx.Memory.UInt32[ctx.IoctlBase + 3].Should().Be(5);
        // End LBA at offset 7
        ctx.Memory.UInt32[ctx.IoctlBase + 7].Should().Be(35);
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

    // IOCTL control codes for MSF-format tests
    private const byte IoctlAudioDiskInfo = 0x04;
    private const byte IoctlAudioTrackInfo = 0x05;
    private const byte IoctlAudioSubchannel = 0x06;

    /// <summary>
    /// A fake image that exposes two tracks so that MSF-related IOCTL responses can be tested.
    /// Track 1: audio, startLba=0; Track 2: data, startLba=75; totalSectors=225.
    /// </summary>
    private sealed class FakeImageWithTracks : ICdRomImage {
        private readonly List<CdTrack> _tracks;

        public FakeImageWithTracks() {
            _tracks = new List<CdTrack> {
                new CdTrack(1, startLba: 0,  lengthSectors: 75,  2352, CdSectorMode.AudioRaw2352, isAudio: true,  0, 0, new NullDataSource(), 0),
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

    private sealed class NullDataSource : IDataSource {
        public long LengthBytes => 0;
        public int Read(long byteOffset, Span<byte> buffer) { buffer.Clear(); return buffer.Length; }
    }

    private sealed class TestContextWithTracks {
        public Memory Memory { get; }
        public State State { get; }
        public MscdexService Mscdex { get; }
        public ICdRomDrive Drive { get; }
        public uint RequestBase { get; }

        public TestContextWithTracks() {
            IMemoryDevice ram = new Ram(A20Gate.EndOfHighMemoryArea);
            AddressReadWriteBreakpoints breakpoints = new();
            A20Gate a20Gate = new(enabled: false);
            Memory = new Memory(breakpoints, ram, a20Gate, initializeResetVector: false);
            State = new State(CpuModel.INTEL_80386);

            ILoggerService logger = Substitute.For<ILoggerService>();
            Mscdex = new MscdexService(State, Memory, logger);

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
    /// IOCTL 0x04 (Get Audio Disk Info): lead-out must be written as 3-byte Red Book MSF
    /// at buffer offsets 3-5, matching DOSBox Staging's mscdex.cpp case 0x04 which calls
    /// TMSF rather than writing a 4-byte DWORD LBA.
    /// Lead-out at LBA 225 → Red Book MSF = 225+150 = 375 frames → (min=0, sec=5, fr=0).
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
        // Lead-out at LBA 225: 225+150 = 375 frames; min=0, sec=5, fr=0
        ctx.Memory.UInt8[ctx.IoctlBase + 3].Should().Be(0,  "lead-out MSF minutes = 0");
        ctx.Memory.UInt8[ctx.IoctlBase + 4].Should().Be(5,  "lead-out MSF seconds = 5");
        ctx.Memory.UInt8[ctx.IoctlBase + 5].Should().Be(0,  "lead-out MSF frames  = 0");
    }

    /// <summary>
    /// IOCTL 0x05 (Get Audio Track Info): track start must be written as 3-byte Red Book MSF
    /// at buffer offsets 2-4 and the attribute at offset 5, matching DOSBox Staging's mscdex.cpp
    /// case 0x05. Previously the start was written as a 4-byte DWORD at offsets 2-5 with the
    /// attribute misplaced at offset 6.
    /// Track 1 starts at LBA 0 → Red Book MSF = 0+150 = 150 frames → (min=0, sec=2, fr=0).
    /// </summary>
    [Fact]
    public void IoctlAudioTrackInfo_WritesStartAsMsfAndAttributeAtOffset5() {
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
        // Track 1 start LBA = 0; Red Book MSF: 0+150=150; min=0, sec=2, fr=0
        ctx.Memory.UInt8[ctx.IoctlBase + 2].Should().Be(0, "track start MSF minutes = 0");
        ctx.Memory.UInt8[ctx.IoctlBase + 3].Should().Be(2, "track start MSF seconds = 2");
        ctx.Memory.UInt8[ctx.IoctlBase + 4].Should().Be(0, "track start MSF frames  = 0");
        ctx.Memory.UInt8[ctx.IoctlBase + 5].Should().Be(0, "attribute = 0 for audio track");
    }

    /// <summary>
    /// IOCTL 0x06 (Get Audio Subchannel): writes attribute, track number, index, relative MSF,
    /// and absolute MSF, matching DOSBox Staging's mscdex.cpp case 0x06 which calls GetAudioSub.
    /// Playing at LBA 80 (inside track 2 which starts at LBA 75):
    ///   absolute MSF = 80+150 = 230 frames → (min=0, sec=3, fr=5)
    ///   relative MSF = 80-75 = 5 LBA → (min=0, sec=0, fr=5)
    /// </summary>
    [Fact]
    public void IoctlAudioSubchannel_WritesProperMsfTrackAndIndex() {
        // Arrange
        TestContextWithTracks ctx = new();
        ctx.Drive.PlayAudio(80, 10); // playing at LBA 80, inside track 2 (starts at 75)
        // Manually advance CurrentLba to 80 (PlayAudio sets StartLba; CurrentLba = StartLba)
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
        // track number = 2
        ctx.Memory.UInt8[ctx.IoctlBase + 2].Should().Be(2, "track number = 2");
        // index = 1
        ctx.Memory.UInt8[ctx.IoctlBase + 3].Should().Be(1, "index = 1");
        // relative MSF: LBA 80 - track2 start 75 = 5 → (0, 0, 5)
        ctx.Memory.UInt8[ctx.IoctlBase + 4].Should().Be(0, "relative min = 0");
        ctx.Memory.UInt8[ctx.IoctlBase + 5].Should().Be(0, "relative sec = 0");
        ctx.Memory.UInt8[ctx.IoctlBase + 6].Should().Be(5, "relative fr  = 5");
        // absolute MSF: LBA 80 + 150 = 230 frames → 230%75=5 fr, 230/75=3 sec, 0 min → (0,3,5)
        ctx.Memory.UInt8[ctx.IoctlBase + 7].Should().Be(0, "absolute min = 0");
        ctx.Memory.UInt8[ctx.IoctlBase + 8].Should().Be(3, "absolute sec = 3");
        ctx.Memory.UInt8[ctx.IoctlBase + 9].Should().Be(5, "absolute fr  = 5");
    }
}
