namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record InstructionPointer {
    public required ushort IP { get; init; }
}
