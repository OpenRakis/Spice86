namespace Spice86.Shared.Emulator.Storage.CdRom.RockRidge;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Decodes Rock Ridge Interchange Protocol (RRIP) metadata from the SUSP entries of an
/// ISO 9660 directory record. This slice handles the NM (alternate name, including
/// CONTINUE concatenation) and PX (POSIX file mode/links/uid/gid) signatures. SL
/// (symbolic link), CE (continuation area), and CL/PL/RE (relocation) are not handled.
/// </summary>
public sealed class RockRidgeParser
{
    private const string NameSignature = "NM";
    private const string PosixSignature = "PX";
    private const byte NameFlagContinue = 0x01;
    private const byte NameFlagCurrent = 0x02;
    private const byte NameFlagParent = 0x04;
    private const int PxOldLength = 32;    // payload bytes for mode/links/uid/gid (8 each)
    private const int PxWithSerialLength = 40; // includes 8-byte serial number

    /// <summary>
    /// Parses Rock Ridge metadata from the System Use Area bytes that follow the name
    /// of an ISO 9660 directory record.
    /// </summary>
    /// <param name="systemUseArea">The raw bytes of the system use area.</param>
    /// <returns>The decoded metadata; fields are null when their SUSP entry is absent.</returns>
    public RockRidgeMetadata Parse(ReadOnlySpan<byte> systemUseArea)
    {
        SuspParser scanner = new();
        IReadOnlyList<SuspEntry> entries = scanner.Parse(systemUseArea);

        StringBuilder? nameBuilder = null;
        uint? mode = null;
        uint? links = null;
        uint? uid = null;
        uint? gid = null;

        for (int i = 0; i < entries.Count; i++)
        {
            SuspEntry entry = entries[i];
            if (entry.Signature == NameSignature)
            {
                AppendName(ref nameBuilder, entry);
            }
            else if (entry.Signature == PosixSignature)
            {
                ReadPosixAttributes(entry, ref mode, ref links, ref uid, ref gid);
            }
        }

        string? alternateName = nameBuilder?.ToString();
        return new RockRidgeMetadata(
            alternateName: alternateName,
            posixFileMode: mode,
            fileLinkCount: links,
            userId: uid,
            groupId: gid);
    }

    private static void AppendName(ref StringBuilder? builder, SuspEntry entry)
    {
        byte[] payload = entry.Payload;
        if (payload.Length < 1)
        {
            return;
        }
        byte flags = payload[0];
        // CURRENT (".") and PARENT ("..") entries describe directory-self/parent and
        // carry no name bytes that should be exposed as an alternate name.
        if ((flags & NameFlagCurrent) != 0 || (flags & NameFlagParent) != 0)
        {
            return;
        }
        if (payload.Length <= 1)
        {
            return;
        }
        string fragment = Encoding.ASCII.GetString(payload, 1, payload.Length - 1);
        if (builder == null)
        {
            builder = new StringBuilder();
        }
        builder.Append(fragment);
        // When the CONTINUE flag is set, the next NM entry must be concatenated by the
        // caller's iteration (already handled by the outer loop appending into `builder`).
        _ = flags & NameFlagContinue;
    }

    private static void ReadPosixAttributes(
        SuspEntry entry,
        ref uint? mode,
        ref uint? links,
        ref uint? uid,
        ref uint? gid)
    {
        byte[] payload = entry.Payload;
        if (payload.Length < PxOldLength)
        {
            return;
        }
        // Each PX field is a "both-byte" 32-bit value: 4 bytes little-endian followed by
        // 4 bytes big-endian (the same value, redundant). The little-endian half is read.
        mode = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(0, 4));
        links = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(8, 4));
        uid = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(16, 4));
        gid = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(24, 4));
        // PxWithSerialLength includes an optional file-serial-number field that is
        // ignored by this slice; the constant is retained for future extension.
        _ = PxWithSerialLength;
    }
}
