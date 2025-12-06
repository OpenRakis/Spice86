namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record DosProgramStateResponse : McpToolResponse {
    public required int CurrentProgramSegmentPrefix { get; init; }

    public required int ParentProgramSegmentPrefix { get; init; }

    public required int EnvironmentTableSegment { get; init; }

    public required int MaximumOpenFiles { get; init; }

    public required int CurrentSizeParagraphs { get; init; }

    public required int CommandTailLength { get; init; }
}