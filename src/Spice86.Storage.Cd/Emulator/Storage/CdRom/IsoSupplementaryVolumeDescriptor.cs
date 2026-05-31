namespace Spice86.Shared.Emulator.Storage.CdRom;

using System;
using System.Collections.Generic;

/// <summary>
/// Represents an ISO 9660 Supplementary Volume Descriptor, typically used to
/// expose the Joliet extension that stores filenames as UCS-2 big-endian and
/// supports identifiers up to 64 UCS-2 characters.
/// </summary>
public sealed class IsoSupplementaryVolumeDescriptor {
    /// <summary>Decoded volume identifier (UCS-2 BE decoded into a .NET string with trailing whitespace trimmed).</summary>
    public string VolumeIdentifier { get; }

    /// <summary>LBA of the root directory record described by this SVD.</summary>
    public int RootDirectoryLba { get; }

    /// <summary>Size in bytes of the root directory described by this SVD.</summary>
    public int RootDirectorySize { get; }

    /// <summary>Logical block size declared in the SVD.</summary>
    public int LogicalBlockSize { get; }

    /// <summary>Volume space size declared in the SVD (sectors).</summary>
    public int VolumeSpaceSize { get; }

    /// <summary>Raw three-byte escape sequence from offset 88 of the SVD sector.</summary>
    public IReadOnlyList<byte> EscapeSequence { get; }

    /// <summary>
    /// True when <see cref="EscapeSequence"/> matches one of the standard Joliet
    /// escape sequences (UCS-2 Level 1/2/3): <c>0x25 0x2F 0x40/0x43/0x45</c>.
    /// </summary>
    public bool IsJoliet { get; }

    /// <summary>Initializes a new <see cref="IsoSupplementaryVolumeDescriptor"/>.</summary>
    public IsoSupplementaryVolumeDescriptor(
        string volumeIdentifier,
        int rootDirectoryLba,
        int rootDirectorySize,
        int logicalBlockSize,
        int volumeSpaceSize,
        IReadOnlyList<byte> escapeSequence) {
        if (escapeSequence is null) {
            throw new ArgumentNullException(nameof(escapeSequence));
        }
        if (escapeSequence.Count != 3) {
            throw new ArgumentException("Escape sequence must be exactly 3 bytes.", nameof(escapeSequence));
        }
        VolumeIdentifier = volumeIdentifier;
        RootDirectoryLba = rootDirectoryLba;
        RootDirectorySize = rootDirectorySize;
        LogicalBlockSize = logicalBlockSize;
        VolumeSpaceSize = volumeSpaceSize;
        EscapeSequence = escapeSequence;
        IsJoliet = IsJolietEscapeSequence(escapeSequence);
    }

    private static bool IsJolietEscapeSequence(IReadOnlyList<byte> sequence) {
        if (sequence[0] != 0x25 || sequence[1] != 0x2F) {
            return false;
        }
        byte level = sequence[2];
        return level is 0x40 or 0x43 or 0x45;
    }
}
