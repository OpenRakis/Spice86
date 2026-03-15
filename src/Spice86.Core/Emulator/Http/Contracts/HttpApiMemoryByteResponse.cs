namespace Spice86.Core.Emulator.Http.Contracts;

/// <summary>
/// Single-byte memory read/write payload.
/// </summary>
public sealed record HttpApiMemoryByteResponse(uint Address, byte Value);
