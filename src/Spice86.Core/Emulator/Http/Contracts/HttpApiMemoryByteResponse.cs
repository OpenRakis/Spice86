namespace Spice86.Core.Emulator.Http.Contracts;

/// <summary>
/// Single-byte memory read/write payload.
/// </summary>
/// <param name="Address">Physical memory address that was read or written.</param>
/// <param name="Value">Byte value at <paramref name="Address"/>.</param>
public sealed record HttpApiMemoryByteResponse(uint Address, byte Value);
