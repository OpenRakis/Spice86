namespace Spice86.Core.Emulator.Mcp.Response;

/// <summary>
/// CPU flags state.
/// </summary>
public sealed record CpuFlags {
    public required bool CarryFlag { get; init; }
    public required bool ParityFlag { get; init; }
    public required bool AuxiliaryFlag { get; init; }
    public required bool ZeroFlag { get; init; }
    public required bool SignFlag { get; init; }
    public required bool DirectionFlag { get; init; }
    public required bool OverflowFlag { get; init; }
    public required bool InterruptFlag { get; init; }
}
