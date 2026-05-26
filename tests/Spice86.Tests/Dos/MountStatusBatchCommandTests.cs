namespace Spice86.Tests.Dos;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
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
using Spice86.Shared.Interfaces;
using Spice86.Tests.Dos.FileSystem;

using System;
using System.Collections.Generic;
using System.IO;

using Xunit;

public sealed class MountStatusBatchCommandTests : IDisposable {
    private readonly string _tempDir;
    private readonly MountStatusContext _ctx;

    public MountStatusBatchCommandTests() {
        _tempDir = Path.Combine(Path.GetTempPath(), $"MountStatusTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _ctx = MountStatusContext.Create(_tempDir);
    }

    public void Dispose() {
        try {
            Directory.Delete(_tempDir, recursive: true);
        } catch (IOException) {
        } catch (UnauthorizedAccessException) {
        }
    }

    [Fact]
    public void Mount_NoArguments_ListsDriveLabels() {
        bool foundDrive = _ctx.DriveManager.TryGetDrive<VirtualDrive>('C', out VirtualDrive? cDrive);

        foundDrive.Should().BeTrue();
        if (cDrive == null) {
            throw new InvalidOperationException("Expected C: drive to be mounted.");
        }
        cDrive.Label = "HOSTDRV";

        string output = ExecuteAndReadOutput("MOUNT", "MOUNT.TXT");
        string expectedMountedPath = _tempDir.Replace('\\', '/');

        output.Should().Contain("Label");
        output.Should().Contain("HOSTDRV");
        output.Should().Contain(expectedMountedPath);
    }

    [Fact]
    public void ImgMount_NoArguments_ListsMountedImageSetAndLabels() {
        string imagePath1 = Path.Combine(_tempDir, "DISK1.IMG");
        string imagePath2 = Path.Combine(_tempDir, "DISK2.IMG");
        byte[] imageData1 = new Fat12ImageBuilder().Build();
        byte[] imageData2 = new Fat12ImageBuilder().Build();
        File.WriteAllBytes(imagePath1, imageData1);
        File.WriteAllBytes(imagePath2, imageData2);

        _ctx.DriveManager.MountFloppyImage('A', imageData1, imagePath1);
        _ctx.DriveManager.AddFloppyImage('A', imageData2, imagePath2);

        bool foundDrive = _ctx.DriveManager.TryGetFloppyDrive('A', out FloppyDiskDrive? floppyDrive);
        foundDrive.Should().BeTrue();
        if (floppyDrive == null) {
            throw new InvalidOperationException("Expected A: floppy drive to be mounted.");
        }
        floppyDrive.Label = "DISKSET";

        string output = ExecuteAndReadOutput("IMGMOUNT", "IMGMOUNT.TXT");

        output.Should().Contain("Label");
        output.Should().Contain("DISKSET");
        output.Should().Contain(imagePath1);
        output.Should().Contain(imagePath2);
    }

    private string ExecuteAndReadOutput(string commandName, string outputFileName) {
        string dosOutputPath = $"C:\\{outputFileName}";

        bool launched = _ctx.Engine.TryExecuteCommandLine($"{commandName} > {dosOutputPath}", out LaunchRequest _);

        launched.Should().BeFalse();

        string hostOutputPath = Path.Combine(_tempDir, outputFileName);
        File.Exists(hostOutputPath).Should().BeTrue();
        return File.ReadAllText(hostOutputPath);
    }

    private sealed class MountStatusContext {
        public required DosBatchExecutionEngine Engine { get; init; }
        public required DosDriveManager DriveManager { get; init; }

        public static MountStatusContext Create(string cDrivePath) {
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

            DosDriveManager driveManager = DosTestHelpers.CreateDriveManager(logger, cDrivePath, null);
            DosMemoryManager memoryManager = new(memory, 0x100, logger);
            DosFileManager fileManager = new(memory, new DosStringDecoder(memory, state, DosCodePageState.CreateForCurrentCulture()), driveManager, logger, new List<IVirtualDevice>());
            IBatchDisplayCommandHandler batchDisplayCommandHandler = new DosBatchDisplayCommandHandler(vgaFunctionality);
            Mscdex mscdex = new(state, memory, logger);
            DosDriveStatusProvider driveStatusProvider = new(driveManager, mscdex);
            CallbackHandler callbackHandler = new(state, logger);

            ISoundChannelCreator channelCreator = Substitute.For<ISoundChannelCreator>();
            channelCreator.AddChannel(Arg.Any<Action<int>>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<HashSet<ChannelFeature>>())
                .Returns(callInfo => new SoundChannel((Action<int>)callInfo[0], (string)callInfo[2], (HashSet<ChannelFeature>)callInfo[3]));

            DosProcessManager processManager = new(
                memory,
                stack,
                state,
                memoryManager,
                fileManager,
                driveManager,
                driveStatusProvider,
                mscdex,
                channelCreator,
                batchDisplayCommandHandler,
                callbackHandler,
                new Dictionary<string, string>(),
                logger);

            return new MountStatusContext {
                Engine = processManager.BatchExecutionEngine,
                DriveManager = driveManager,
            };
        }
    }
}