namespace Spice86.Core.Emulator.Mcp.Response;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// A single disassembled instruction.
/// </summary>
internal sealed record DisassemblyLine {
    /// <summary>
    /// Segmented address of the instruction.
    /// </summary>
    public required SegmentedAddress Address { get; init; }

    /// <summary>
    /// Hex-encoded raw bytes of the instruction.
    /// </summary>
    public required string Bytes { get; init; }

    /// <summary>
    /// Assembly text representation of the instruction.
    /// </summary>
    public required string Assembly { get; init; }
}
