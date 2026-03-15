namespace Spice86.Core.Emulator.Http.Contracts;

/// <summary>
/// Status payload returned by the HTTP API.
/// </summary>
/// <param name="IsPaused">Whether the emulator is currently paused.</param>
/// <param name="IsCpuRunning">Whether the CPU execution loop is active.</param>
/// <param name="Cycles">Total number of CPU cycles executed so far.</param>
/// <param name="Cs">Current value of the CS (code segment) register.</param>
/// <param name="Ip">Current value of the IP (instruction pointer) register.</param>
/// <param name="IpPhysicalAddress">Physical address of the current instruction (CS*16 + IP with A20 wrapping).</param>
/// <param name="MemorySizeBytes">Total size of emulated memory in bytes.</param>
public sealed record HttpApiStatusResponse(
    bool IsPaused,
    bool IsCpuRunning,
    long Cycles,
    ushort Cs,
    ushort Ip,
    uint IpPhysicalAddress,
    int MemorySizeBytes);
