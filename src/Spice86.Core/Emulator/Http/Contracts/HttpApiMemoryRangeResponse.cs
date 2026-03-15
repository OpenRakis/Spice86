namespace Spice86.Core.Emulator.Http.Contracts;

using System.Collections.Generic;

/// <summary>
/// Memory range read payload. <see cref="Values"/> is serialized as a JSON array of byte integers.
/// </summary>
/// <param name="Address">Physical start address of the range.</param>
/// <param name="Length">Number of bytes returned; may be less than requested if the range reaches the end of memory.</param>
/// <param name="Values">Byte values starting at <paramref name="Address"/>.</param>
public sealed record HttpApiMemoryRangeResponse(uint Address, int Length, IReadOnlyList<byte> Values);
