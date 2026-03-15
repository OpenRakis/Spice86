namespace Spice86.Core.Emulator.Http.Contracts;

/// <summary>
/// Single-byte memory write request.
/// </summary>
public sealed class HttpApiWriteByteRequest {
    /// <summary>Byte value to write at the target memory address.</summary>
    public byte Value { get; set; }
}
