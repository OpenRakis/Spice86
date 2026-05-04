namespace Spice86.Tests.Dos;

using NSubstitute;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;
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
    internal static DosDriveManager CreateDriveManager(ILoggerService logger, string? cDrive, string? exe = null) {
        return new DosDriveManager(logger, cDrive, exe, CreateMediaIdTable());
    }

    /// <summary>Creates a <see cref="DosDriveManager"/> with a substituted logger and a dummy media ID table.</summary>
    internal static DosDriveManager CreateDriveManager(string? cDrive, string? exe = null) {
        return CreateDriveManager(Substitute.For<ILoggerService>(), cDrive, exe);
    }
}
