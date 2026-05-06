namespace Spice86.Tests.Dos;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.InterruptHandlers.Mscdex;
using Spice86.Core.Emulator.InterruptHandlers.VGA;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Batch;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;
using Spice86.Tests.Dos.FileSystem;

using System;
using System.Collections.Generic;

using Xunit;

/// <summary>
/// Tests for the BOOT.COM internal batch command and the
/// <see cref="DosProcessManager.BootFromFloppy"/> CPU/memory setup.
///
/// Mirrors DOSBox Staging's BOOT command for floppy images: the first
/// 512-byte sector of the mounted image must be loaded at physical 0x7C00,
/// and the CPU must be prepared with CS:IP=0000:7C00, SS:SP=0000:7C00 and
/// DL set to the floppy drive number (0=A, 1=B).
/// </summary>
public class BootFloppyTests {
    [Fact]
    public void BootFromFloppy_NoMountedImage_Fails() {
        BootContext ctx = BootContext.Create();

        DosExecResult result = ctx.ProcessManager.BootFromFloppy('A');

        result.Success.Should().BeFalse();
    }

    [Fact]
    public void BootFromFloppy_HardDiskLetter_Fails() {
        BootContext ctx = BootContext.Create();
        byte[] image = new Fat12ImageBuilder().Build();
        ctx.DriveManager.MountFloppyImage('A', image, "test.img");

        DosExecResult result = ctx.ProcessManager.BootFromFloppy('C');

        result.Success.Should().BeFalse();
    }

    [Fact]
    public void BootFromFloppy_DriveA_LoadsSectorAt7C00() {
        BootContext ctx = BootContext.Create();
        byte[] image = new Fat12ImageBuilder().Build();
        // Mark a recognisable byte pattern in the boot-code area (offset 0x40, well past the BPB).
        image[0x40] = 0xDE;
        image[0x41] = 0xAD;
        image[0x42] = 0xBE;
        image[0x43] = 0xEF;
        ctx.DriveManager.MountFloppyImage('A', image, "test.img");

        DosExecResult result = ctx.ProcessManager.BootFromFloppy('A');

        result.Success.Should().BeTrue();
        ctx.Memory.UInt8[0x7C00 + 0x40].Should().Be(0xDE);
        ctx.Memory.UInt8[0x7C00 + 0x41].Should().Be(0xAD);
        ctx.Memory.UInt8[0x7C00 + 0x42].Should().Be(0xBE);
        ctx.Memory.UInt8[0x7C00 + 0x43].Should().Be(0xEF);
        ctx.Memory.UInt8[0x7C00 + 510].Should().Be(0x55);
        ctx.Memory.UInt8[0x7C00 + 511].Should().Be(0xAA);
    }

    [Fact]
    public void BootFromFloppy_DriveA_SetsCpuStateForBiosBootProtocol() {
        BootContext ctx = BootContext.Create();
        byte[] image = new Fat12ImageBuilder().Build();
        ctx.DriveManager.MountFloppyImage('A', image, "test.img");

        ctx.ProcessManager.BootFromFloppy('A');

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
        BootContext ctx = BootContext.Create();
        byte[] image = new Fat12ImageBuilder().Build();
        ctx.DriveManager.MountFloppyImage('B', image, "test.img");

        ctx.ProcessManager.BootFromFloppy('B');

        ctx.State.DL.Should().Be(0x01, "DL must be 1 for boot drive B:");
    }

    [Fact]
    public void BootFromFloppy_MissingBootSignature_Fails() {
        BootContext ctx = BootContext.Create();
        byte[] image = new Fat12ImageBuilder().Build();
        image[510] = 0x00;
        image[511] = 0x00;
        ctx.DriveManager.MountFloppyImage('A', image, "test.img");

        DosExecResult result = ctx.ProcessManager.BootFromFloppy('A');

        result.Success.Should().BeFalse();
    }

    [Fact]
    public void BatchEngine_BootCommand_NoMount_FailsWithErrorMessage() {
        BootContext ctx = BootContext.Create();
        DosBatchExecutionEngine engine = ctx.ProcessManager.BatchExecutionEngine;

        bool launched = engine.TryExecuteCommandLine("BOOT", out LaunchRequest launchRequest);

        launched.Should().BeFalse("missing floppy mount must keep batch execution going");
        launchRequest.Should().BeOfType<ContinueBatchExecutionLaunchRequest>();
    }

    [Fact]
    public void BatchEngine_BootCommand_WithMountedFloppy_YieldsBootFloppyLaunchRequest() {
        BootContext ctx = BootContext.Create();
        byte[] image = new Fat12ImageBuilder().Build();
        ctx.DriveManager.MountFloppyImage('A', image, "test.img");
        DosBatchExecutionEngine engine = ctx.ProcessManager.BatchExecutionEngine;

        bool launched = engine.TryExecuteCommandLine("BOOT -l A", out LaunchRequest launchRequest);

        launched.Should().BeTrue("BOOT must yield a launch request when the floppy is valid");
        launchRequest.Should().BeOfType<BootFloppyLaunchRequest>();
        ((BootFloppyLaunchRequest)launchRequest).DriveLetter.Should().Be('A');
    }

    [Fact]
    public void BatchEngine_BootCommand_RejectsHardDiskLetter() {
        BootContext ctx = BootContext.Create();
        DosBatchExecutionEngine engine = ctx.ProcessManager.BatchExecutionEngine;

        bool launched = engine.TryExecuteCommandLine("BOOT -l C", out LaunchRequest launchRequest);

        launched.Should().BeFalse();
        launchRequest.Should().BeOfType<ContinueBatchExecutionLaunchRequest>();
    }

    private sealed class BootContext {
        public required Memory Memory { get; init; }
        public required DosProcessManager ProcessManager { get; init; }
        public required State State { get; init; }
        public required DosDriveManager DriveManager { get; init; }

        public static BootContext Create() {
            ILoggerService logger = Substitute.For<ILoggerService>();
            IMemoryDevice ram = new Ram(A20Gate.EndOfHighMemoryArea);
            AddressReadWriteBreakpoints memoryBreakpoints = new();
            AddressReadWriteBreakpoints ioPortBreakpoints = new();
            A20Gate a20Gate = new(enabled: false);
            State state = new(CpuModel.INTEL_80386);
            Memory memory = new(memoryBreakpoints, ram, a20Gate, initializeResetVector: true);
            Stack stack = new(memory, state);
            IOPortDispatcher ioPortDispatcher = new(ioPortBreakpoints, state, logger, false);
            BiosDataArea biosDataArea = new(memory, 640);
            Spice86.Core.Emulator.CPU.InterruptVectorTable interruptVectorTable = new(memory);
            VgaRom vgaRom = new();
            VgaFunctionality vgaFunctionality = new(memory, interruptVectorTable, ioPortDispatcher, biosDataArea, vgaRom, true);

            DosDriveManager driveManager = DosTestHelpers.CreateDriveManager(logger, null);
            DosMemoryManager memoryManager = new(memory, 0x100, logger);
            DosFileManager fileManager = new(memory, new DosStringDecoder(memory, state), driveManager, logger, new List<IVirtualDevice>());
            IBatchDisplayCommandHandler batchDisplayCommandHandler = new DosBatchDisplayCommandHandler(vgaFunctionality);
            MscdexService mscdex = new(state, memory, logger);

            ISoundChannelCreator channelCreator = Substitute.For<ISoundChannelCreator>();
            channelCreator.AddChannel(Arg.Any<Action<int>>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<HashSet<ChannelFeature>>())
                .Returns(callInfo => new SoundChannel((Action<int>)callInfo[0], (string)callInfo[2], (HashSet<ChannelFeature>)callInfo[3]));

            DosProcessManager processManager = new(
                memory, stack, state,
                memoryManager, fileManager, driveManager,
                mscdex, channelCreator, batchDisplayCommandHandler,
                new Dictionary<string, string>(), logger);

            return new BootContext {
                Memory = memory,
                ProcessManager = processManager,
                State = state,
                DriveManager = driveManager,
            };
        }
    }
}

