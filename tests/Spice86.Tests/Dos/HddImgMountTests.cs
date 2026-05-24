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
using Spice86.Shared.Emulator.Storage.FileSystem;
using Spice86.Shared.Emulator.Storage.FileSystem.Partitions;
using Spice86.Shared.Interfaces;
using Spice86.Tests.Dos.FileSystem;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Xunit;

/// <summary>
/// TDD integration tests for IMGMOUNT <c>-t hdd</c> handling and partition-aware
/// FAT mounting of MBR-partitioned hard-disk images. Verifies the second
/// Phase 7 atom: cross-cutting MOUNT/IMGMOUNT HDD parity plus multi-partition
/// filesystem access from a single host image file.
/// </summary>
public class HddImgMountTests : System.IDisposable {
    private const int SectorSize = 512;

    private readonly List<string> _tempFiles = new();

    public void Dispose() {
        foreach (string path in _tempFiles) {
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            } catch (IOException) {
                // best-effort cleanup
            }
        }
    }

    [Fact]
    public void MountHardDiskImage_MissingMbrSignature_ReturnsFalse() {
        // Arrange
        HddMountContext ctx = HddMountContext.Create();
        byte[] image = BuildPartitionedDiskWithFat12(bootableLba: 63, fileName: "HELLO.TXT", fileContent: "HI"u8.ToArray());
        image[510] = 0x00;
        image[511] = 0x00;

        // Act
        bool mounted = ctx.DriveManager.MountHardDiskImage('C', image, "hdd.img");

        // Assert
        mounted.Should().BeFalse();
        ctx.DriveManager.TryGetFloppyDrive('C', out FloppyDiskDrive? _).Should().BeFalse();
    }

    [Fact]
    public void MountHardDiskImage_NoUsablePartition_ReturnsFalse() {
        // Arrange
        HddMountContext ctx = HddMountContext.Create();
        byte[] image = new byte[256 * SectorSize];
        image[510] = 0x55;
        image[511] = 0xAA;
        // MBR with zero partitions; FindFirstNonEmpty returns null.

        // Act
        bool mounted = ctx.DriveManager.MountHardDiskImage('C', image, "hdd.img");

        // Assert
        mounted.Should().BeFalse();
    }

    [Fact]
    public void MountHardDiskImage_PartitionedFat12_ExposesPartitionFatToDriveImage() {
        // Arrange
        HddMountContext ctx = HddMountContext.Create();
        byte[] image = BuildPartitionedDiskWithFat12(bootableLba: 63, fileName: "README.TXT", fileContent: "PARTITION-DATA"u8.ToArray());

        // Act
        bool mounted = ctx.DriveManager.MountHardDiskImage('C', image, "hdd.img");

        // Assert
        mounted.Should().BeTrue();
        ctx.DriveManager.TryGetFloppyDrive('C', out FloppyDiskDrive? drive).Should().BeTrue();
        drive!.Image.Should().NotBeNull("the partition's FAT must be loaded as the drive's image view");
        FatFileSystem fs = drive.Image!;
        bool found = fs.TryGetEntry("README.TXT", out FatDirectoryEntry? entry);
        found.Should().BeTrue();
        byte[] content = fs.ReadFile(entry!);
        Encoding.ASCII.GetString(content).Should().Be("PARTITION-DATA");
    }

    [Fact]
    public void ImgMount_HddType_MountsPartitionedImage_AndDriveIsAvailable() {
        // Arrange
        HddMountContext ctx = HddMountContext.Create();
        byte[] image = BuildPartitionedDiskWithFat12(bootableLba: 63, fileName: "INSTALL.EXE", fileContent: "MZ"u8.ToArray());
        string hostPath = WriteTempImage(image, ".hdd");
        DosBatchExecutionEngine engine = ctx.ProcessManager.BatchExecutionEngine;

        // Act
        bool launched = engine.TryExecuteCommandLine($"IMGMOUNT C \"{hostPath}\" -t hdd", out LaunchRequest launchRequest);

        // Assert
        launched.Should().BeFalse("IMGMOUNT is a host-side command and must not yield a launch request");
        launchRequest.Should().BeOfType<ContinueBatchExecutionLaunchRequest>();
        ctx.DriveManager.TryGetFloppyDrive('C', out FloppyDiskDrive? drive).Should().BeTrue();
        drive!.Image.Should().NotBeNull();
        drive.Image!.TryGetEntry("INSTALL.EXE", out FatDirectoryEntry? entry).Should().BeTrue();
        entry!.FileSize.Should().Be(2u);
    }

    [Fact]
    public void ImgMount_HddType_AfterMount_BootCommandLoadsPartitionBootSectorAt7C00() {
        // Arrange
        HddMountContext ctx = HddMountContext.Create();
        byte[] image = BuildPartitionedDiskWithFat12(bootableLba: 63, fileName: "BOOT.SYS", fileContent: "BOOT"u8.ToArray());
        // Stamp a recognisable byte deep inside the partition's first sector (FAT BPB area is at offset 0..63).
        int partitionStart = 63 * SectorSize;
        image[partitionStart + 0x100] = 0xBB;
        string hostPath = WriteTempImage(image, ".hdd");
        DosBatchExecutionEngine engine = ctx.ProcessManager.BatchExecutionEngine;
        engine.TryExecuteCommandLine($"IMGMOUNT C \"{hostPath}\" -t hdd", out LaunchRequest _);

        // Act
        bool booted = engine.TryExecuteCommandLine("BOOT -l C", out LaunchRequest bootRequest);

        // Assert
        booted.Should().BeTrue();
        bootRequest.Should().BeOfType<BootHddLaunchRequest>();
        // Execute the boot via the HardDiskBootService to confirm the partition's
        // first sector (not the MBR) is what lands at 0000:7C00.
        ctx.BootService.TryBootFromHardDiskImage(image, 0x80, hostPath).Should().BeTrue();
        ctx.Memory.UInt8[0x7C00 + 0x100].Should().Be(0xBB);
        ctx.Memory.UInt8[0x7C00 + 510].Should().Be(0x55);
        ctx.Memory.UInt8[0x7C00 + 511].Should().Be(0xAA);
        ctx.State.DL.Should().Be(0x80);
    }

    [Fact]
    public void ImgMount_HddType_MissingFileExtension_AutoDetectsViaMbrSignature() {
        // Arrange
        HddMountContext ctx = HddMountContext.Create();
        byte[] image = BuildPartitionedDiskWithFat12(bootableLba: 63, fileName: "AUTO.DAT", fileContent: "OK"u8.ToArray());
        string hostPath = WriteTempImage(image, ".img"); // .img extension would auto-detect as floppy without -t hdd
        DosBatchExecutionEngine engine = ctx.ProcessManager.BatchExecutionEngine;

        // Act
        engine.TryExecuteCommandLine($"IMGMOUNT C \"{hostPath}\" -t hdd", out LaunchRequest _);

        // Assert
        ctx.DriveManager.TryGetFloppyDrive('C', out FloppyDiskDrive? drive).Should().BeTrue();
        drive!.Image.Should().NotBeNull();
        drive.Image!.TryGetEntry("AUTO.DAT", out FatDirectoryEntry? _).Should().BeTrue();
    }

    private string WriteTempImage(byte[] image, string extension) {
        string path = Path.Combine(Path.GetTempPath(), $"spice86_hdd_{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, image);
        _tempFiles.Add(path);
        return path;
    }

    private static byte[] BuildPartitionedDiskWithFat12(uint bootableLba, string fileName, byte[] fileContent) {
        // Build a FAT12 image via the existing builder (contains a valid 0xAA55 boot sector),
        // then embed it at the bootable partition's LBA inside a larger MBR-partitioned disk.
        byte[] fatImage = new Fat12ImageBuilder().WithFile(fileName, fileContent).Build();
        uint sectorCount = (uint)(fatImage.Length / SectorSize);
        int totalSectors = (int)(bootableLba + sectorCount + 1);
        byte[] disk = new byte[totalSectors * SectorSize];
        Array.Copy(fatImage, 0, disk, (int)(bootableLba * SectorSize), fatImage.Length);

        List<PartitionTableEntry> entries = new() {
            new PartitionTableEntry(0x80, 0x01, bootableLba, sectorCount),
        };
        MasterBootRecord mbr = new(entries);
        MbrCodec.Write(mbr, disk.AsSpan(0, SectorSize));
        return disk;
    }

    private sealed class HddMountContext {
        public required Memory Memory { get; init; }
        public required DosProcessManager ProcessManager { get; init; }
        public required State State { get; init; }
        public required DosDriveManager DriveManager { get; init; }
        public required HardDiskBootService BootService { get; init; }

        public static HddMountContext Create() {
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

            return new HddMountContext {
                Memory = memory,
                ProcessManager = processManager,
                State = state,
                DriveManager = driveManager,
                BootService = bootService,
            };
        }
    }
}
