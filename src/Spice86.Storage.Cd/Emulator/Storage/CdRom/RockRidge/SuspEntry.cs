namespace Spice86.Shared.Emulator.Storage.CdRom.RockRidge;

/// <summary>
/// Immutable record describing a single System Use Sharing Protocol (SUSP) entry as
/// found in the trailing System Use Area of an ISO 9660 directory record.
/// </summary>
/// <remarks>
/// SUSP entries share a common 4-byte header: a 2-character ASCII signature
/// (for example <c>NM</c>, <c>PX</c>, <c>SP</c>, <c>ST</c>), a 1-byte total length
/// (header + payload), and a 1-byte version. The remaining bytes form the entry-specific
/// payload, exposed as a defensively copied byte array so callers may not mutate the
/// underlying disc buffer.
/// </remarks>
public sealed class SuspEntry
{
    /// <summary>Gets the two-character ASCII signature (for example "NM", "PX", "SP", "ST").</summary>
    public string Signature { get; }

    /// <summary>Gets the version byte from the SUSP header (typically 1).</summary>
    public byte Version { get; }

    /// <summary>Gets the entry payload (the bytes that follow the 4-byte header).</summary>
    public byte[] Payload { get; }

    /// <summary>Initialises a new <see cref="SuspEntry"/> with all header fields.</summary>
    /// <param name="signature">The two-character signature.</param>
    /// <param name="version">The version byte.</param>
    /// <param name="payload">The payload bytes; a defensive copy is stored.</param>
    public SuspEntry(string signature, byte version, byte[] payload)
    {
        Signature = signature;
        Version = version;
        Payload = (byte[])payload.Clone();
    }
}
