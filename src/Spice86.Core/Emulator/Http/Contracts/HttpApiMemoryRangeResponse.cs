namespace Spice86.Core.Emulator.Http.Contracts;

using System.Collections.Generic;

/// <summary>
/// Memory range read payload. <see cref="Values"/> is serialized as a JSON array of byte integers.
/// </summary>
public sealed record HttpApiMemoryRangeResponse(uint Address, int Length, IReadOnlyList<byte> Values);
