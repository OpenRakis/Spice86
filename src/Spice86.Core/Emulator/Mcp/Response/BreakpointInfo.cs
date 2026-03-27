namespace Spice86.Core.Emulator.Mcp.Response;

using Spice86.Shared.Emulator.VM.Breakpoint;

using System.Text.Json.Serialization;

internal sealed record BreakpointInfo {
    public required string Id { get; init; }

    public required long Address { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required BreakPointType Type { get; init; }

    public string? Condition { get; init; }

    public required bool IsEnabled { get; init; }
}
