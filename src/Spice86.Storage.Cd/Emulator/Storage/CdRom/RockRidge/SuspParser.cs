namespace Spice86.Shared.Emulator.Storage.CdRom.RockRidge;

using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Iterates the System Use Sharing Protocol (SUSP) entries embedded in the System Use
/// Area of an ISO 9660 directory record.
/// </summary>
/// <remarks>
/// Each SUSP entry starts with a 4-byte header (2-byte ASCII signature, 1-byte total
/// length, 1-byte version). Parsing halts at the first malformed length (zero or larger
/// than the remaining buffer) and at the canonical "ST" terminator. Continuation Areas
/// (CE) are not followed in this slice.
/// </remarks>
public sealed class SuspParser
{
    private const string TerminatorSignature = "ST";
    private const int HeaderSize = 4;

    /// <summary>
    /// Enumerates SUSP entries from the system-use-area bytes that follow the name
    /// (and optional padding) of an ISO 9660 directory record.
    /// </summary>
    /// <param name="systemUseArea">The raw bytes of the system use area.</param>
    /// <returns>The discovered entries in disc order, stopping at "ST" or end-of-buffer.</returns>
    public IReadOnlyList<SuspEntry> Parse(ReadOnlySpan<byte> systemUseArea)
    {
        List<SuspEntry> entries = new();
        int offset = 0;
        while (offset + HeaderSize <= systemUseArea.Length)
        {
            byte length = systemUseArea[offset + 2];
            if (length < HeaderSize)
            {
                break;
            }
            if (offset + length > systemUseArea.Length)
            {
                break;
            }
            string signature = Encoding.ASCII.GetString(systemUseArea.Slice(offset, 2));
            byte version = systemUseArea[offset + 3];
            byte[] payload = systemUseArea.Slice(offset + HeaderSize, length - HeaderSize).ToArray();
            entries.Add(new SuspEntry(signature, version, payload));
            if (signature == TerminatorSignature)
            {
                break;
            }
            offset += length;
        }
        return entries;
    }
}
