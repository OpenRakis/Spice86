namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record GeneralPurposeRegisters {
    public required uint EAX { get; init; }

    public required uint EBX { get; init; }

    public required uint ECX { get; init; }

    public required uint EDX { get; init; }

    public required uint ESI { get; init; }

    public required uint EDI { get; init; }

    public required uint ESP { get; init; }

    public required uint EBP { get; init; }
}
