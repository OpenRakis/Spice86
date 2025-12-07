namespace Spice86.Core.Emulator.Mcp.Response;

public sealed record XmsStateResponse : McpToolResponse {
    public required bool IsEnabled { get; init; }
    public required int TotalMemoryKB { get; init; }
    public required int FreeMemoryKB { get; init; }
    public required int LargestBlockKB { get; init; }
    public required bool HmaAvailable { get; init; }
    public required bool HmaAllocated { get; init; }
    public required int AllocatedBlocks { get; init; }
    public required XmsHandleInfo[] Handles { get; init; }
}
