namespace Spice86.Core.Emulator.Mcp.Response;

internal record CpuRegistersResponse : McpToolResponse {
    public required GeneralPurposeRegisters GeneralPurpose { get; init; }

    public required SegmentRegisters Segments { get; init; }

    public required InstructionPointer InstructionPointer { get; init; }

    public required CpuFlags Flags { get; init; }
}
