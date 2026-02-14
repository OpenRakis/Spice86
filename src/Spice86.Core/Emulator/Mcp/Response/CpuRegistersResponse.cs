namespace Spice86.Core.Emulator.Mcp.Response;

/// <summary>
/// Response for CPU registers query.
/// </summary>
public sealed record CpuRegistersResponse : McpToolResponse {
    /// <summary>
    /// Gets the general purpose registers.
    /// </summary>
    public required GeneralPurposeRegisters GeneralPurpose { get; init; }

    /// <summary>
    /// Gets the segment registers.
    /// </summary>
    public required SegmentRegisters Segments { get; init; }

    /// <summary>
    /// Gets the instruction pointer.
    /// </summary>
    public required InstructionPointer InstructionPointer { get; init; }

    /// <summary>
    /// Gets the CPU flags.
    /// </summary>
    public required CpuFlags Flags { get; init; }
}
