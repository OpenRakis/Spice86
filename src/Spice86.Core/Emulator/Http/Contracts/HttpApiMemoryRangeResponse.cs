namespace Spice86.Core.Emulator.Http.Contracts;

/// <summary>
/// Memory range read payload.
/// </summary>
public sealed record HttpApiMemoryRangeResponse(uint Address, int Length, byte[] Values);
