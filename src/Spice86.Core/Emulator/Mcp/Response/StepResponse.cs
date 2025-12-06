namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record StepResponse : McpToolResponse
{
    public required bool Success { get; init; }

    public required string Message { get; init; }

    public required GeneralPurposeRegisters GeneralPurpose { get; init; }

    public required SegmentRegisters Segments { get; init; }

    public required InstructionPointer InstructionPointer { get; init; }

    public required CpuFlags Flags { get; init; }
}
