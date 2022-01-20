namespace Spice86.Emulator;

using Spice86.Emulator.Function;

/// <summary> Configuration for spice86, that is what to run and how. Set on startup. </summary>
public record Configuration {
    public string? CDrive { get; init; }

    public string? DefaultDumpDirectory { get; init; }

    public string? Exe { get; init; }
    public string? ExeArgs { get; init; }

    public byte[]? ExpectedChecksum { get; init; }

    public bool FailOnUnhandledPort { get; init; }

    public int? GdbPort { get; init; }

    public bool InstallInterruptVector { get; init; }

    public IOverrideSupplier? OverrideSupplier { get; init; }

    public int ProgramEntryPointSegment { get; init; }

    public bool UseCodeOverride { get; init; }

    /// <summary>
    /// Only for <see cref="Devices.Timer.Timer"/>
    /// </summary>
    public long InstructionsPerSecond { get; init; }

    public double TimeMultiplier { get; init; }
}