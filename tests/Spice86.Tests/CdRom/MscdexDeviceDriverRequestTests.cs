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
}
