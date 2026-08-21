namespace Spice86.Tests.Dos;

using Microsoft.Extensions.Logging;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.InterruptHandlers.Mscdex;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
/// Factory helpers shared across DOS unit tests.
/// </summary>
internal static class DosTestHelpers {
    /// <summary>
    /// Creates a <see cref="DosMediaIdTable"/> backed by a plain byte array,
    /// suitable for tests that do not exercise media-descriptor logic.
    /// </summary>
    internal static DosMediaIdTable CreateMediaIdTable() {
        byte[] buffer = new byte[DosMediaIdTable.TableSizeInBytes];
        return new DosMediaIdTable(new ByteArrayReaderWriter(buffer), 0, 0xC800);
    }

    /// <summary>
    /// Creates a <see cref="DosDriveManager"/> with a dummy media ID table.
    /// </summary>
    internal static DosDriveManager CreateDriveManager(ILogger logger, string? cDrive, string? exe = null) {
        IMemory memory = new Memory(new(), new Ram(0x200000), new A20Gate(), new RealModeMmu386(), false);
        State state = new(CpuModel.INTEL_80286);
        SoftwareMixer mixer = new(Audio.Filters.AudioEngine.Dummy, new PauseHandler(logger));
        DriveActivityNotifier driveActivityNotifier = new();
        Mscdex mscdex = new Mscdex(state, memory, logger, driveActivityNotifier);
        return new DosDriveManager(mscdex, mixer, driveActivityNotifier,  cDrive, exe, CreateMediaIdTable(), logger);
    }

    /// <summary>Creates a <see cref="DosDriveManager"/> with a substituted logger and a dummy media ID table.</summary>
    internal static DosDriveManager CreateDriveManager(string? cDrive, string? exe = null) {
        return CreateDriveManager(Substitute.For<ILogger>(), cDrive, exe);
    }
}
