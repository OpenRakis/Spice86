namespace Spice86.Core.Emulator.Platform;

/// <summary>Platform-specific BIOS memory reporting.</summary>
/// <remarks>
/// Tandy boundaries follow DOSBox Staging, copyright the DOSBox Team and DOSBox Staging Team,
/// licensed under GPL-2.0-or-later.
/// </remarks>
public sealed class MachineProfile {
    private static readonly MachineProfile PcProfile = new(MachineType.Pc, 640, 640);
    private static readonly MachineProfile TandyProfile = new(MachineType.Tandy1000, 624, 640);

    private MachineProfile(MachineType machineType, ushort biosReportedMemoryKb, ushort trueMemoryKb) {
        MachineType = machineType;
        BiosReportedMemoryKb = biosReportedMemoryKb;
        TrueMemoryKb = trueMemoryKb;
    }

    /// <summary>Selected guest platform.</summary>
    public MachineType MachineType { get; }

    /// <summary>Whether native Tandy hardware is selected.</summary>
    public bool IsTandy => MachineType == MachineType.Tandy1000;

    /// <summary>Conventional memory reported by INT 12h.</summary>
    public ushort BiosReportedMemoryKb { get; }

    /// <summary>Conventional RAM backing before platform reservations.</summary>
    public ushort TrueMemoryKb { get; }

    /// <summary>Resolves the immutable profile for a supported guest platform.</summary>
    public static MachineProfile For(MachineType machineType) => machineType switch {
        MachineType.Pc => PcProfile,
        MachineType.Tandy1000 => TandyProfile,
        _ => throw new ArgumentOutOfRangeException(nameof(machineType), machineType, "Unsupported guest platform.")
    };
}