namespace Spice86.Core.Emulator.Http.Contracts;

/// <summary>
/// Status payload returned by the HTTP API.
/// </summary>
public sealed record HttpApiStatusResponse(
    bool IsPaused,
    bool IsCpuRunning,
    long Cycles,
    ushort Cs,
    ushort Ip,
    uint IpPhysicalAddress,
    int MemorySizeBytes);
