namespace Spice86.Core.Emulator.Mcp.Response;

internal sealed record McpAboutResponse : McpToolResponse {
    public required string Name { get; init; }

    public required string Version { get; init; }

    public required string Purpose { get; init; }

    public required bool Stateless { get; init; }

    public required string McpEndpoint { get; init; }

    public required string HealthEndpoint { get; init; }

    public required string[] CapabilityScopes { get; init; }

    public required string ExtensionModel { get; init; }

    public required string[] ExtensionPoints { get; init; }

    public required string[] Discovery { get; init; }

    public required int ToolCount { get; init; }
}
