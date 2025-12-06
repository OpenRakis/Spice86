namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record CpuStatusResponse {
    public required GeneralPurposeRegisters GeneralPurpose { get; init; }

    public required SegmentRegisters Segments { get; init; }

    public required InstructionPointer InstructionPointer { get; init; }

    public required CpuFlags Flags { get; init; }

    public required long Cycles { get; init; }
}
