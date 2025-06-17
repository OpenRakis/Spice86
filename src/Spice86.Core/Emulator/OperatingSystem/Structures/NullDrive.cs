namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Shared.Interfaces;

/// <summary>
/// Represents an empty drive.
/// </summary>
public sealed class NullDrive : FolderDrive {
    public NullDrive(ILoggerService loggerService, string name,
        byte unitCount, string signature = "",
        ushort strategy = 0, ushort interrupt = 0)
        : base(loggerService, name, unitCount, signature,
            strategy, interrupt) {
    }

    public override bool IsRemovable => true;
}
