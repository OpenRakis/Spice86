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

using System;
using System.Collections.Generic;
using System.IO;

using Xunit;

/// <summary>
/// Tests for the SUBST internal batch command, mirroring DOSBox Staging's
/// <c>SUBST</c> behaviour: substitute a drive letter for a path, remove an
/// existing SUBST with <c>/D</c>, or list active SUBSTs when invoked with
/// no arguments.
/// </summary>
public sealed class SubstBatchCommandTests : IDisposable {
    private readonly string _tempDir;
    private readonly SubstContext _ctx;

    public SubstBatchCommandTests() {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SubstTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _ctx = SubstContext.Create(_tempDir);
    }

    public void Dispose() {
        try {
            Directory.Delete(_tempDir, recursive: true);
        } catch (IOException) {
        } catch (UnauthorizedAccessException) {
        }
    }

    [Fact]
    public void Subst_WithDosPath_CreatesVirtualDrive() {
        string subFolder = Path.Combine(_tempDir, "games");
        Directory.CreateDirectory(subFolder);

        bool launched = _ctx.Engine.TryExecuteCommandLine("SUBST D: C:\\games", out LaunchRequest _);

        launched.Should().BeFalse();
        _ctx.DriveManager.TryGetValue('D', out VirtualDrive? drive).Should().BeTrue();
        drive!.MountedHostDirectory.Should().Contain("games");
        _ctx.DriveManager.IsSubstDrive('D').Should().BeTrue();
    }

    [Fact]
    public void Subst_WithMissingPath_DoesNotCreateDrive() {
        bool launched = _ctx.Engine.TryExecuteCommandLine("SUBST D: C:\\does_not_exist", out LaunchRequest _);

        launched.Should().BeFalse();
        _ctx.DriveManager.TryGetValue('D', out _).Should().BeFalse();
    }

    [Fact]
    public void Subst_WithSlashD_RemovesSubstDrive() {
        string subFolder = Path.Combine(_tempDir, "games");
        Directory.CreateDirectory(subFolder);
        _ctx.Engine.TryExecuteCommandLine("SUBST D: C:\\games", out LaunchRequest _);

        bool launched = _ctx.Engine.TryExecuteCommandLine("SUBST D: /D", out LaunchRequest _);

        launched.Should().BeFalse();
        _ctx.DriveManager.IsSubstDrive('D').Should().BeFalse();
        _ctx.DriveManager.TryGetValue('D', out _).Should().BeFalse();
    }

    [Fact]
    public void Subst_SlashDOnNonSubstDrive_FailsGracefully() {
        bool launched = _ctx.Engine.TryExecuteCommandLine("SUBST E: /D", out LaunchRequest _);

        launched.Should().BeFalse();
    }

    [Fact]
    public void Subst_OnFloppyLetter_Rejected() {
        string subFolder = Path.Combine(_tempDir, "games");
        Directory.CreateDirectory(subFolder);

        _ctx.Engine.TryExecuteCommandLine("SUBST A: C:\\games", out LaunchRequest _);

        _ctx.DriveManager.IsSubstDrive('A').Should().BeFalse();
    }

    [Fact]
    public void Subst_OnDriveAlreadyMounted_Rejected() {
        // C: is already mounted to _tempDir by DosTestHelpers.CreateDriveManager.
        string subFolder = Path.Combine(_tempDir, "games");
        Directory.CreateDirectory(subFolder);

        _ctx.Engine.TryExecuteCommandLine("SUBST C: C:\\games", out LaunchRequest _);

        _ctx.DriveManager.IsSubstDrive('C').Should().BeFalse();
    }

    [Fact]
    public void Subst_NoArguments_ListsActiveSubsts() {
        string subFolder = Path.Combine(_tempDir, "games");
        Directory.CreateDirectory(subFolder);
        _ctx.Engine.TryExecuteCommandLine("SUBST D: C:\\games", out LaunchRequest _);

        bool launched = _ctx.Engine.TryExecuteCommandLine("SUBST", out LaunchRequest _);

        launched.Should().BeFalse();
        _ctx.DriveManager.SubstDrives.Should().ContainKey('D');
    }

    [Fact]
    public void DosDriveManager_MountSubstDrive_TracksDriveAndOriginalPath() {
        _ctx.DriveManager.MountSubstDrive('E', _tempDir, "C:\\GAMES");

        _ctx.DriveManager.IsSubstDrive('E').Should().BeTrue();
        _ctx.DriveManager.SubstDrives['E'].Should().Be("C:\\GAMES");
    }

    [Fact]
    public void DosDriveManager_UnmountSubstDrive_RemovesEntry() {
        _ctx.DriveManager.MountSubstDrive('E', _tempDir, "C:\\GAMES");

        bool removed = _ctx.DriveManager.UnmountSubstDrive('E');

        removed.Should().BeTrue();
        _ctx.DriveManager.IsSubstDrive('E').Should().BeFalse();
        _ctx.DriveManager.TryGetValue('E', out _).Should().BeFalse();
    }

    [Fact]
    public void DosDriveManager_UnmountSubstDrive_OnNonSubstDrive_ReturnsFalse() {
        bool removed = _ctx.DriveManager.UnmountSubstDrive('Z');

        removed.Should().BeFalse();
    }

    private sealed class SubstContext {
        public required DosBatchExecutionEngine Engine { get; init; }
        public required DosDriveManager DriveManager { get; init; }

        public static SubstContext Create(string cDrivePath) {
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
            InterruptVectorTable interruptVectorTable = new(memory);
            VgaRom vgaRom = new();
            VgaFunctionality vgaFunctionality = new(memory, interruptVectorTable, ioPortDispatcher, biosDataArea, vgaRom, true);

            DosDriveManager driveManager = DosTestHelpers.CreateDriveManager(logger, cDrivePath, null);
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

            return new SubstContext {
                Engine = processManager.BatchExecutionEngine,
                DriveManager = driveManager,
            };
        }
    }
}
