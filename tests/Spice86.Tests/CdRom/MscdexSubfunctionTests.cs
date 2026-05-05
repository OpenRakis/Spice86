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

/// <summary>
/// TDD tests for MSCDEX subfunctions, verifying DOSBox Staging register conventions.
/// </summary>
public sealed class MscdexSubfunctionTests {
    private const ushort BufferSegment = 0x2000;
    private const ushort BufferOffset = 0x0000;

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

        public TestContext() {
            IMemoryDevice ram = new Ram(A20Gate.EndOfHighMemoryArea);
            AddressReadWriteBreakpoints breakpoints = new();
            A20Gate a20Gate = new(enabled: false);
            Memory = new Memory(breakpoints, ram, a20Gate, initializeResetVector: false);
            State = new State(CpuModel.INTEL_80386);
            ILoggerService logger = Substitute.For<ILoggerService>();
            Mscdex = new MscdexService(State, Memory, logger);
        }

        public void AddDriveAtIndex(char letter, byte driveIndex) {
            ICdRomDrive drive = new CdRomDrive(new FakeImage());
            Mscdex.AddDrive(new MscdexDriveEntry(letter, driveIndex, drive));
        }
    }

    /// <summary>
    /// AL=0x00 (Install check): DOSBox Staging sets BX = drive count, CX = first drive index, AL = 0xFF.
    /// </summary>
    [Fact]
    public void GetNumberOfCdRomDrives_SetsBxCxAndAlFF() {
        // Arrange
        TestContext ctx = new();
        ctx.AddDriveAtIndex('D', 3);
        ctx.State.AL = 0x00;

        // Act
        ctx.Mscdex.Dispatch();

        // Assert
        ctx.State.BX.Should().Be(1, "BX = number of drives");
        ctx.State.CX.Should().Be(3, "CX = first drive index (D: = 3)");
        ctx.State.AL.Should().Be(0xFF, "AL = 0xFF per DOSBox Staging MSCDEX_Handler 0x1500 case");
    }

    /// <summary>
    /// AL=0x00 (Install check): When no drives are registered, BX=0 and CX=0 and AL=0xFF.
    /// </summary>
    [Fact]
    public void GetNumberOfCdRomDrives_NoDrives_SetsBxZeroAndAlFF() {
        // Arrange
        TestContext ctx = new();
        ctx.State.AL = 0x00;

        // Act
        ctx.Mscdex.Dispatch();

        // Assert
        ctx.State.BX.Should().Be(0, "BX = 0 when no drives");
        ctx.State.CX.Should().Be(0, "CX = 0 when no drives");
        ctx.State.AL.Should().Be(0xFF, "AL = 0xFF regardless");
    }

    /// <summary>
    /// AL=0x0B (CD-ROM drive check): DOSBox Staging reads drive from CX, writes BX=0xADAD, AX=0x5AD8 when valid.
    /// </summary>
    [Fact]
    public void CdRomDriveCheck_ValidDriveInCx_WritesBxAdAdAndAxValidMagic() {
        // Arrange
        TestContext ctx = new();
        ctx.AddDriveAtIndex('D', 3);
        ctx.State.AL = 0x0B;
        ctx.State.CX = 3; // drive index for D:

        // Act
        ctx.Mscdex.Dispatch();

        // Assert
        ctx.State.BX.Should().Be(0xADAD, "BX = 0xADAD always (per DOSBox Staging case 0x150B)");
        ctx.State.AX.Should().Be(0x5AD8, "AX = 0x5AD8 when drive is a valid CD-ROM (per DOSBox Staging case 0x150B)");
    }

    /// <summary>
    /// AL=0x0B (CD-ROM drive check): When drive is not a CD-ROM, BX=0xADAD, AX=0x0000.
    /// </summary>
    [Fact]
    public void CdRomDriveCheck_InvalidDriveInCx_WritesBxAdAdAndAxZero() {
        // Arrange
        TestContext ctx = new();
        ctx.State.AL = 0x0B;
        ctx.State.CX = 5; // no drive at index 5

        // Act
        ctx.Mscdex.Dispatch();

        // Assert
        ctx.State.BX.Should().Be(0xADAD, "BX = 0xADAD even when drive is not found");
        ctx.State.AX.Should().Be(0x0000, "AX = 0x0000 when drive is not a CD-ROM");
    }

    /// <summary>
    /// AL=0x08 (Absolute disk read): DOSBox Staging uses CX=drive, SI:DI=sector, DX=count, ES:BX=buffer.
    /// Verifies that CX selects the correct drive.
    /// </summary>
    [Fact]
    public void AbsoluteDiskRead_UsesCxAsDriveAndSiDiAsSector() {
        // Arrange
        TestContext ctx = new();
        ctx.AddDriveAtIndex('D', 3);
        ctx.State.AL = 0x08;
        ctx.State.CX = 3;   // drive index D:
        ctx.State.SI = 0;   // sector high word
        ctx.State.DI = 16;  // sector low word (combined = LBA 16 = PVD sector)
        ctx.State.DX = 1;   // number of sectors to read
        ctx.State.ES = BufferSegment;
        ctx.State.BX = BufferOffset;

        // Act
        ctx.Mscdex.Dispatch();

        // Assert
        ctx.State.CarryFlag.Should().BeFalse("read should succeed for a valid drive");
    }

    /// <summary>
    /// AL=0x08 (Absolute disk read): When CX refers to an unknown drive, carry flag is set.
    /// </summary>
    [Fact]
    public void AbsoluteDiskRead_InvalidDriveInCx_SetsCarryFlag() {
        // Arrange
        TestContext ctx = new();
        ctx.State.AL = 0x08;
        ctx.State.CX = 7;  // no drive at index 7
        ctx.State.SI = 0;
        ctx.State.DI = 0;
        ctx.State.DX = 1;

        // Act
        ctx.Mscdex.Dispatch();

        // Assert
        ctx.State.CarryFlag.Should().BeTrue("CF set when the drive is not found");
    }

    /// <summary>
    /// Dispatch() clears carry flag at entry for all subfunctions, per DOSBox Staging CALLBACK_SCF(false).
    /// </summary>
    [Fact]
    public void Dispatch_ClearsCarryFlagBeforeDispatch() {
        // Arrange
        TestContext ctx = new();
        ctx.AddDriveAtIndex('D', 3);
        ctx.State.CarryFlag = true;
        ctx.State.AL = 0x0C; // GetMscdexVersion — a no-fail function

        // Act
        ctx.Mscdex.Dispatch();

        // Assert
        ctx.State.CarryFlag.Should().BeFalse("Dispatch() must clear CF before invoking subfunctions");
    }

    /// <summary>
    /// AL=0x05 (Read VTOC): DOSBox Staging uses CX=drive and DX=volume descriptor index.
    /// Drive index in CX and descriptor index in DX should select the correct drive and sector.
    /// </summary>
    [Fact]
    public void ReadVolumeTableOfContents_UsesCxAsDriveAndDxAsDescriptorIndex() {
        // Arrange
        TestContext ctx = new();
        ctx.AddDriveAtIndex('D', 3);
        ctx.State.AL = 0x05;
        ctx.State.CX = 3;   // drive index for D: (matching DOSBox Staging reg_cx = drive)
        ctx.State.DX = 0;   // first descriptor (PVD) — DOSBox Staging reg_dx = descriptor index
        ctx.State.BP = 99;  // BP must NOT be used as drive index
        ctx.State.ES = BufferSegment;
        ctx.State.BX = BufferOffset;

        // Act
        ctx.Mscdex.Dispatch();

        // Assert
        ctx.State.CarryFlag.Should().BeFalse("reading PVD from a valid drive should succeed");
    }

    /// <summary>
    /// AL=0x05 (Read VTOC): When drive CX is unknown, carry flag is set and AX contains the error.
    /// </summary>
    [Fact]
    public void ReadVolumeTableOfContents_InvalidDriveInCx_SetsCarryFlag() {
        // Arrange
        TestContext ctx = new();
        ctx.State.AL = 0x05;
        ctx.State.CX = 7;  // no drive at index 7
        ctx.State.DX = 0;
        ctx.State.ES = BufferSegment;
        ctx.State.BX = BufferOffset;

        // Act
        ctx.Mscdex.Dispatch();

        // Assert
        ctx.State.CarryFlag.Should().BeTrue("CF set when drive is not found");
    }

    /// <summary>
    /// AL=0x06/0x07 (debugging on/off): DOSBox Staging does nothing — no carry, no error,
    /// registers unchanged.
    /// </summary>
    [Theory]
    [InlineData(0x06)]
    [InlineData(0x07)]
    public void DebuggingOnOff_DoesNothing(byte subfunction) {
        // Arrange
        TestContext ctx = new();
        ctx.State.AH = 0xBE; // Set AH first to avoid overwriting AL
        ctx.State.AL = subfunction;

        // Act
        ctx.Mscdex.Dispatch();

        // Assert
        ctx.State.CarryFlag.Should().BeFalse("no error should be signalled for debugging on/off");
        ctx.State.AH.Should().Be(0xBE, "AH must not be modified by a no-op");
    }

    /// <summary>
    /// AL=0x0A (reserved): DOSBox Staging does nothing — no carry, no error, registers unchanged.
    /// </summary>
    [Fact]
    public void Reserved_0x0A_DoesNothing() {
        // Arrange
        TestContext ctx = new();
        ctx.State.AH = 0xCA; // Set AH first to avoid overwriting AL
        ctx.State.AL = 0x0A;

        // Act
        ctx.Mscdex.Dispatch();

        // Assert
        ctx.State.CarryFlag.Should().BeFalse("no error should be signalled for reserved subfunction");
        ctx.State.AH.Should().Be(0xCA, "AH must not be modified by a no-op");
    }

    /// <summary>
    /// AL=0x0E (GetSetVolumeDescriptorPreference) BX=0 (get): DOSBox Staging returns DX=0x100
    /// without modifying BX, for a valid drive in CX.
    /// </summary>
    [Fact]
    public void GetVolumeDescriptorPreference_ValidDrive_ReturnsDx100() {
        // Arrange
        TestContext ctx = new();
        ctx.AddDriveAtIndex('D', 3);
        ctx.State.AL = 0x0E;
        ctx.State.CX = 3;   // valid drive
        ctx.State.BX = 0;   // get preference
        ctx.State.DX = 0;

        // Act
        ctx.Mscdex.Dispatch();

        // Assert
        ctx.State.CarryFlag.Should().BeFalse("get preference on a valid drive should succeed");
        ctx.State.DX.Should().Be(0x0100, "DX must be 0x100 (prefer PVD) per DOSBox Staging");
    }

    /// <summary>
    /// AL=0x0E (GetSetVolumeDescriptorPreference) with invalid drive in CX: carry flag set.
    /// </summary>
    [Fact]
    public void GetVolumeDescriptorPreference_InvalidDrive_SetsCarryFlag() {
        // Arrange
        TestContext ctx = new();
        ctx.State.AL = 0x0E;
        ctx.State.CX = 7;   // no drive at index 7
        ctx.State.BX = 0;

        // Act
        ctx.Mscdex.Dispatch();

        // Assert
        ctx.State.CarryFlag.Should().BeTrue("CF set when drive is not found");
    }

    /// <summary>
    /// AL=0x09 (AbsoluteDiskWrite): DOSBox Staging returns AX=1 (MSCDEX_ERROR_INVALID_FUNCTION)
    /// with carry set since CD-ROMs are read-only.
    /// </summary>
    [Fact]
    public void AbsoluteDiskWrite_ReturnsInvalidFunctionError() {
        // Arrange
        TestContext ctx = new();
        ctx.State.AL = 0x09;

        // Act
        ctx.Mscdex.Dispatch();

        // Assert
        ctx.State.CarryFlag.Should().BeTrue("CF set since CD-ROM writes are not supported");
        ctx.State.AX.Should().Be(1, "AX=1 is MSCDEX_ERROR_INVALID_FUNCTION per DOSBox Staging");
    }
}
