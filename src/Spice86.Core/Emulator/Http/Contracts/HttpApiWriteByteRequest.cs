namespace Spice86.Core.Emulator.Http.Contracts;

/// <summary>
/// Single-byte memory write request.
/// </summary>
public sealed class HttpApiWriteByteRequest {
    public byte Value { get; set; }
}
