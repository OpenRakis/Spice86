namespace Spice86.Core.Emulator.Mcp.Response;

/// <summary>
/// Instruction pointer state.
/// </summary>
public sealed record InstructionPointer {
    public required ushort IP { get; init; }
}
