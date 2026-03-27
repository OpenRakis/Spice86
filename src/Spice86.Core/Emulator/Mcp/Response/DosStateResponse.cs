namespace Spice86.Core.Emulator.Mcp.Response;

using System.Collections.Generic;

internal sealed record DosStateResponse {
    public required string CurrentDrive { get; init; }

    public required int CurrentDriveIndex { get; init; }

    public required int PotentialDriveLetters { get; init; }

    public required int CurrentProgramSegmentPrefix { get; init; }

    public required int DeviceCount { get; init; }

    public required bool HasEms { get; init; }

    public required bool HasXms { get; init; }

    public required IReadOnlyList<DosDriveResponse> Drives { get; init; }
}