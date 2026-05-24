namespace Spice86.Tests.Dos;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.Boot;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.InterruptHandlers.Mscdex;
using Spice86.Core.Emulator.InterruptHandlers.VGA;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Batch;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Storage.FileSystem.Partitions;
using Spice86.Shared.Interfaces;
using Spice86.Tests.Dos.FileSystem;

using System;
using System.Collections.Generic;

using Xunit;

/// <summary>
/// TDD tests for the hard-disk boot path (BOOT.COM with a drive letter C: or above).
/// Mirrors DOSBox Staging's HDD boot in <c>boot.cpp</c>: read MBR, pick the
/// bootable partition (boot indicator 0x80) or fall back to the first non-empty
/// partition, load that partition's boot sector to 0x7C00, set DL to 0x80,
/// then jump to <c>0000:7C00</c>.
/// </summary>
public class BootHardDiskTests {
    private const int SectorSize = 512;

    [Fact]
    public void BootFromHardDisk_NoImage_ReturnsFalse() {
        // Arrange
        HddBootContext ctx = HddBootContext.Create();

        // Act
        bool ok = ctx.BootService.TryBootFromHardDiskImage(null, 0x80, null);

        // Assert
        ok.Should().BeFalse();
    }

    [Fact]
    public void BootFromHardDisk_MissingMbrSignature_ReturnsFalse() {
        // Arrange
        HddBootContext ctx = HddBootContext.Create();
        byte[] image = BuildPartitionedImage(bootableLba: 1, fallbackLba: 0);
        image[510] = 0x00;
        image[511] = 0x00;

        // Act
        bool ok = ctx.BootService.TryBootFromHardDiskImage(image, 0x80, "hdd.img");

        // Assert
        ok.Should().BeFalse();
    }

    [Fact]
    public void BootFromHardDisk_BootIndicatorPartition_LoadsPartitionBootSectorAt7C00() {
        // Arrange
        HddBootContext ctx = HddBootContext.Create();
        const uint bootableLba = 63;
        byte[] image = BuildPartitionedImage(bootableLba: bootableLba, fallbackLba: 0);
        // Stamp recognisable bytes inside the bootable partition's first sector body.
        int partitionStart = (int)(bootableLba * SectorSize);
        image[partitionStart + 0x40] = 0xC0;
        image[partitionStart + 0x41] = 0xFF;
        image[partitionStart + 0x42] = 0xEE;
        image[partitionStart + 0x43] = 0xED;

        // Act
        bool ok = ctx.BootService.TryBootFromHardDiskImage(image, 0x80, "hdd.img");

        // Assert
        ok.Should().BeTrue();
        ctx.Memory.UInt8[0x7C00 + 0x40].Should().Be(0xC0);
        ctx.Memory.UInt8[0x7C00 + 0x41].Should().Be(0xFF);
        ctx.Memory.UInt8[0x7C00 + 0x42].Should().Be(0xEE);
        ctx.Memory.UInt8[0x7C00 + 0x43].Should().Be(0xED);
        ctx.Memory.UInt8[0x7C00 + 510].Should().Be(0x55);
        ctx.Memory.UInt8[0x7C00 + 511].Should().Be(0xAA);
    }

    [Fact]
    public void BootFromHardDisk_SetsCpuStateForBiosBootProtocol() {
        // Arrange
        HddBootContext ctx = HddBootContext.Create();
        byte[] image = BuildPartitionedImage(bootableLba: 1, fallbackLba: 0);

        // Act
        ctx.BootService.TryBootFromHardDiskImage(image, 0x80, "hdd.img");

        // Assert
        ctx.State.CS.Should().Be(0);
        ctx.State.IP.Should().Be(0x7C00);
        ctx.State.DS.Should().Be(0);
        ctx.State.ES.Should().Be(0);
        ctx.State.SS.Should().Be(0);
        ctx.State.SP.Should().Be(0x7C00);
        ctx.State.DL.Should().Be(0x80, "DL must be 0x80 for first HDD boot drive");
        ctx.State.AX.Should().Be(0);
        ctx.State.CX.Should().Be(1);
        ctx.State.BX.Should().Be(0x7C00);
        ctx.State.BP.Should().Be(0);
        ctx.State.SI.Should().Be(0);
        ctx.State.DI.Should().Be(0);
        ctx.State.InterruptFlag.Should().BeTrue();
    }

    [Fact]
    public void BootFromHardDisk_NoBootIndicator_FallsBackToFirstNonEmptyPartition() {
        // Arrange
        HddBootContext ctx = HddBootContext.Create();
        const uint fallbackLba = 5;
        byte[] image = BuildPartitionedImage(bootableLba: 0, fallbackLba: fallbackLba);
        int partitionStart = (int)(fallbackLba * SectorSize);
        image[partitionStart + 0x10] = 0xAB;
        image[partitionStart + 0x11] = 0xCD;

        // Act
        bool ok = ctx.BootService.TryBootFromHardDiskImage(image, 0x80, "hdd.img");

        // Assert
        ok.Should().BeTrue();
        ctx.Memory.UInt8[0x7C00 + 0x10].Should().Be(0xAB);
        ctx.Memory.UInt8[0x7C00 + 0x11].Should().Be(0xCD);
    }

    [Fact]
    public void BootFromHardDisk_NoPartitions_ReturnsFalse() {
        // Arrange
        HddBootContext ctx = HddBootContext.Create();
        byte[] image = BuildPartitionedImage(bootableLba: 0, fallbackLba: 0);

        // Act
        bool ok = ctx.BootService.TryBootFromHardDiskImage(image, 0x80, "hdd.img");

        // Assert
        ok.Should().BeFalse();
    }

    [Fact]
    public void BatchEngine_BootCommand_WithMountedHddImage_YieldsBootHddLaunchRequest() {
        // Arrange
        HddBootContext ctx = HddBootContext.Create();
        byte[] image = new Fat12ImageBuilder().Build();
        ctx.DriveManager.MountFloppyImage('C', image, "c.img");
        DosBatchExecutionEngine engine = ctx.ProcessManager.BatchExecutionEngine;

        // Act
        bool launched = engine.TryExecuteCommandLine("BOOT -l C", out LaunchRequest launchRequest);

        // Assert
        launched.Should().BeTrue();
        launchRequest.Should().BeOfType<BootHddLaunchRequest>();
        ((BootHddLaunchRequest)launchRequest).DriveLetter.Should().Be('C');
    }

    private static byte[] BuildPartitionedImage(uint bootableLba, uint fallbackLba) {
        // Disk layout: 256 sectors. Each requested partition gets a synthetic
        // boot sector with the standard 0xAA55 signature so the boot service
        // can validate it independently from the MBR.
        const int totalSectors = 256;
        byte[] image = new byte[totalSectors * SectorSize];

        List<PartitionTableEntry> entries = new();
        if (bootableLba > 0) {
            entries.Add(new PartitionTableEntry(0x80, 0x06, bootableLba, 64));
            WritePartitionBootSector(image, bootableLba);
        }
        if (fallbackLba > 0) {
            entries.Add(new PartitionTableEntry(0x00, 0x06, fallbackLba, 64));
            WritePartitionBootSector(image, fallbackLba);
        }

        MasterBootRecord mbr = new(entries);
        MbrCodec.Write(mbr, image.AsSpan(0, SectorSize));
        return image;
    }

    private static void WritePartitionBootSector(byte[] image, uint lbaStart) {
        int offset = (int)(lbaStart * SectorSize);
        image[offset + SectorSize - 2] = 0x55;
        image[offset + SectorSize - 1] = 0xAA;
    }

    private sealed class HddBootContext {
        public required Memory Memory { get; init; }
        public required DosProcessManager ProcessManager { get; init; }
        public required State State { get; init; }
        public required DosDriveManager DriveManager { get; init; }
        public required HardDiskBootService BootService { get; init; }

        public static HddBootContext Create() {
            ILoggerService logger = Substitute.For<ILoggerService>();
            IMemoryDevice ram = new Ram(A20Gate.EndOfHighMemoryArea);
            AddressReadWriteBreakpoints memoryBreakpoints = new();
            AddressReadWriteBreakpoints ioPortBreakpoints = new();
            A20Gate a20Gate = new(enabled: false);
            State state = new(CpuModel.INTEL_80386);
            Memory memory = new(memoryBreakpoints, ram, a20Gate, new RealModeMmu386(), initializeResetVector: true);
            Stack stack = new(memory, state);
            IOPortDispatcher ioPortDispatcher = new(ioPortBreakpoints, state, logger, false);
            BiosDataArea biosDataArea = new(memory, 640);
            InterruptVectorTable interruptVectorTable = new(memory);
            VgaRom vgaRom = new();
            VgaFunctionality vgaFunctionality = new(memory, interruptVectorTable, ioPortDispatcher, biosDataArea, vgaRom, true);

            DosDriveManager driveManager = DosTestHelpers.CreateDriveManager(logger, null);
            DosMemoryManager memoryManager = new(memory, 0x100, logger);
            DosFileManager fileManager = new(memory, new DosStringDecoder(memory, state, DosCodePageState.CreateForCurrentCulture()), driveManager, logger, new List<IVirtualDevice>());
            IBatchDisplayCommandHandler batchDisplayCommandHandler = new DosBatchDisplayCommandHandler(vgaFunctionality);
            Mscdex mscdex = new(state, memory, logger);

            ISoundChannelCreator channelCreator = Substitute.For<ISoundChannelCreator>();
            channelCreator.AddChannel(Arg.Any<Action<int>>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<HashSet<ChannelFeature>>())
                .Returns(callInfo => new SoundChannel((Action<int>)callInfo[0], (string)callInfo[2], (HashSet<ChannelFeature>)callInfo[3]));

            DosProcessManager processManager = new(
                memory, stack, state,
                memoryManager, fileManager, driveManager,
                mscdex, channelCreator, batchDisplayCommandHandler,
                new Dictionary<string, string>(), logger);

            HardDiskBootService bootService = new(memory, state, logger);

            return new HddBootContext {
                Memory = memory,
                ProcessManager = processManager,
                State = state,
                DriveManager = driveManager,
                BootService = bootService,
            };
        }
    }
}
