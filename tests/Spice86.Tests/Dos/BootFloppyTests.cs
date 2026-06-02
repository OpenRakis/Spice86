namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator.OperatingSystem.Batch;
using Spice86.Tests.Dos.FileSystem;

using System;

using Xunit;

/// <summary>
/// Tests for the BOOT.COM internal batch command and the
/// <see cref="FloppyBootService"/> CPU/memory setup.
///
/// BOOT command for floppy images: the first
/// 512-byte sector of the mounted image must be loaded at physical 0x7C00,
/// and the CPU must be prepared with CS:IP=0000:7C00, SS:SP=0000:7C00 and
/// DL set to the floppy drive number (0=A, 1=B). The boot service lives
/// outside the DOS namespace because PC booters do not use the emulator's
/// DOS kernel.
/// </summary>
public class BootFloppyTests {
    [Fact]
    public void BootFromFloppy_DriveA_LoadsSectorAt7C00() {
        using DosTestFixture ctx = new(Path.GetTempPath());
        byte[] image = new Fat12ImageBuilder().Build();
        // Mark a recognisable byte pattern in the boot-code area (offset 0x40, well past the BPB).
        image[0x40] = 0xDE;
        image[0x41] = 0xAD;
        image[0x42] = 0xBE;
        image[0x43] = 0xEF;

        bool ok = ctx.BootService.TryBootFromFloppyImage(image, 0, "test.img");

        ok.Should().BeTrue();
        ctx.Memory.UInt8[0x7C00 + 0x40].Should().Be(0xDE);
        ctx.Memory.UInt8[0x7C00 + 0x41].Should().Be(0xAD);
        ctx.Memory.UInt8[0x7C00 + 0x42].Should().Be(0xBE);
        ctx.Memory.UInt8[0x7C00 + 0x43].Should().Be(0xEF);
        ctx.Memory.UInt8[0x7C00 + 510].Should().Be(0x55);
        ctx.Memory.UInt8[0x7C00 + 511].Should().Be(0xAA);
    }

    [Fact]
    public void BootFromFloppy_DriveA_SetsCpuStateForBiosBootProtocol() {
        using DosTestFixture ctx = new(Path.GetTempPath());
        byte[] image = new Fat12ImageBuilder().Build();

        ctx.BootService.TryBootFromFloppyImage(image, 0, "test.img");

        ctx.State.CS.Should().Be(0);
        ctx.State.IP.Should().Be(0x7C00);
        ctx.State.DS.Should().Be(0);
        ctx.State.ES.Should().Be(0);
        ctx.State.SS.Should().Be(0);
        ctx.State.SP.Should().Be(0x7C00);
        ctx.State.DL.Should().Be(0x00, "DL must be 0 for boot drive A:");
        ctx.State.AX.Should().Be(0);
        ctx.State.CX.Should().Be(1);
        ctx.State.BX.Should().Be(0x7C00);
        ctx.State.BP.Should().Be(0);
        ctx.State.SI.Should().Be(0);
        ctx.State.DI.Should().Be(0);
        ctx.State.InterruptFlag.Should().BeTrue();
    }

    [Fact]
    public void BootFromFloppy_DriveB_SetsDLToOne() {
        using DosTestFixture ctx = new(Path.GetTempPath());
        byte[] image = new Fat12ImageBuilder().Build();

        ctx.BootService.TryBootFromFloppyImage(image, 1, "test.img");

        ctx.State.DL.Should().Be(0x01, "DL must be 1 for boot drive B:");
    }

    [Fact]
    public void BootFromFloppy_MissingBootSignature_ReturnsFalse() {
        using DosTestFixture ctx = new(Path.GetTempPath());
        byte[] image = new Fat12ImageBuilder().Build();
        image[510] = 0x00;
        image[511] = 0x00;

        bool ok = ctx.BootService.TryBootFromFloppyImage(image, 0, "test.img");

        ok.Should().BeFalse();
    }

    [Fact]
    public void BatchEngine_BootCommand_NoMount_FailsWithErrorMessage() {
        using DosTestFixture ctx = new(Path.GetTempPath());
        DosBatchExecutionEngine engine = ctx.ProcessManager.BatchExecutionEngine;

        bool launched = engine.TryExecuteCommandLine("BOOT", out LaunchRequest launchRequest);

        launched.Should().BeFalse("missing floppy mount must keep batch execution going");
        launchRequest.Should().BeOfType<ContinueBatchExecutionLaunchRequest>();
    }

    [Fact]
    public void BatchEngine_BootCommand_WithMountedFloppy_YieldsBootFloppyLaunchRequest() {
        using DosTestFixture ctx = new(Path.GetTempPath());
        byte[] image = new Fat12ImageBuilder().Build();
        ctx.DriveManager.MountFloppyImage('A', image, "test.img");
        DosBatchExecutionEngine engine = ctx.ProcessManager.BatchExecutionEngine;

        bool launched = engine.TryExecuteCommandLine("BOOT -l A", out LaunchRequest launchRequest);

        launched.Should().BeTrue("BOOT must yield a launch request when the floppy is valid");
        launchRequest.Should().BeOfType<BootFloppyLaunchRequest>();
        ((BootFloppyLaunchRequest)launchRequest).DriveLetter.Should().Be('A');
    }

    [Fact]
    public void BatchEngine_BootCommand_HardDiskLetterWithoutMount_FailsGracefully() {
        // Arrange
        using DosTestFixture ctx = new(Path.GetTempPath());
        DosBatchExecutionEngine engine = ctx.ProcessManager.BatchExecutionEngine;

        // Act
        bool launched = engine.TryExecuteCommandLine("BOOT -l C", out LaunchRequest launchRequest);

        // Assert
        launched.Should().BeFalse();
        launchRequest.Should().BeOfType<ContinueBatchExecutionLaunchRequest>();
    }
}

