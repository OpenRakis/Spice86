namespace Spice86.Storage.Tests.CdRom;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Fluent builder for synthetic ISO 9660 images used in storage tests. Produces
/// a valid raw ISO containing the system area, a Primary Volume Descriptor, an
/// optional Joliet supplementary volume descriptor, a volume-descriptor-set
/// terminator, root directories for each requested volume, and file data
/// extents. The output is byte-for-byte parseable by <see cref="Spice86.Shared.Emulator.Storage.CdRom.IsoImage"/>
/// without any external dependency on mkisofs or DOSBox staging fixtures.
/// </summary>
/// <remarks>
/// Layout produced by <see cref="Build"/>:
/// <list type="number">
///   <item><description>Sectors 0..15: system area (zeros).</description></item>
///   <item><description>Sector 16: Primary Volume Descriptor.</description></item>
///   <item><description>Sector 17: Joliet SVD (only when <see cref="WithJolietVolumeIdentifier"/> is called).</description></item>
///   <item><description>Next sector: volume-descriptor-set terminator.</description></item>
///   <item><description>Next sector: primary root directory (ASCII filenames).</description></item>
///   <item><description>Next sector: Joliet root directory (UCS-2 BE filenames) when present.</description></item>
///   <item><description>One sector per file extent thereafter.</description></item>
/// </list>
/// </remarks>
internal sealed class Iso9660ImageBuilder
{
    private const int SectorSize = 2048;
    private const int SystemAreaSectors = 16;

    private string _primaryVolumeId = "TEST";
    private string? _jolietVolumeId;
    private readonly List<FileEntry> _files = new();

    /// <summary>Sets the volume identifier stored in the Primary Volume Descriptor (ASCII, padded to 32 bytes).</summary>
    public Iso9660ImageBuilder WithVolumeIdentifier(string identifier)
    {
        _primaryVolumeId = identifier;
        return this;
    }

    /// <summary>
    /// Enables a Joliet Supplementary Volume Descriptor with the supplied identifier
    /// (encoded UCS-2 big-endian, padded to 32 bytes / 16 UCS-2 code points).
    /// </summary>
    public Iso9660ImageBuilder WithJolietVolumeIdentifier(string identifier)
    {
        _jolietVolumeId = identifier;
        return this;
    }

    /// <summary>
    /// Adds a regular file to both root directories. The ASCII name lands in
    /// the primary directory; the Joliet name lands in the Joliet directory
    /// (only emitted when <see cref="WithJolietVolumeIdentifier"/> has been called).
    /// </summary>
    /// <param name="primaryName">8.3 ASCII name (e.g. <c>HELLO.TXT</c>); the builder appends <c>;1</c> per spec.</param>
    /// <param name="jolietName">Long filename to store UCS-2 BE in the Joliet directory.</param>
    /// <param name="contents">File payload; must fit in a single sector for this builder.</param>
    public Iso9660ImageBuilder WithFile(string primaryName, string jolietName, byte[] contents)
    {
        if (contents.Length > SectorSize)
        {
            throw new ArgumentException("Test builder only supports single-sector files.", nameof(contents));
        }
        _files.Add(new FileEntry(primaryName, jolietName, contents));
        return this;
    }

    /// <summary>Serialises the configured layout into a raw ISO 9660 byte array.</summary>
    public byte[] Build()
    {
        bool hasJoliet = _jolietVolumeId is not null;
        int pvdLba = 16;
        int svdLba = pvdLba + 1;
        int terminatorLba = hasJoliet ? svdLba + 1 : svdLba;
        int primaryRootLba = terminatorLba + 1;
        int jolietRootLba = hasJoliet ? primaryRootLba + 1 : -1;
        int firstFileLba = hasJoliet ? jolietRootLba + 1 : primaryRootLba + 1;

        int totalSectors = firstFileLba + _files.Count;
        byte[] image = new byte[totalSectors * SectorSize];

        WritePrimaryVolumeDescriptor(image, pvdLba, primaryRootLba);
        if (hasJoliet)
        {
            WriteJolietVolumeDescriptor(image, svdLba, jolietRootLba);
        }
        WriteVolumeDescriptorSetTerminator(image, terminatorLba);

        WritePrimaryRootDirectory(image, primaryRootLba, firstFileLba);
        if (hasJoliet)
        {
            WriteJolietRootDirectory(image, jolietRootLba, firstFileLba);
        }
        for (int i = 0; i < _files.Count; i++)
        {
            int fileLba = firstFileLba + i;
            Buffer.BlockCopy(_files[i].Contents, 0, image, fileLba * SectorSize, _files[i].Contents.Length);
        }
        return image;
    }

    private void WritePrimaryVolumeDescriptor(byte[] image, int lba, int rootLba)
    {
        Span<byte> sector = image.AsSpan(lba * SectorSize, SectorSize);
        sector[0] = 0x01; // type: PVD
        WriteAscii(sector.Slice(1, 5), "CD001");
        sector[6] = 0x01; // version
        WriteAsciiPadded(sector.Slice(40, 32), _primaryVolumeId, padding: (byte)' ');
        BinaryPrimitives.WriteInt32LittleEndian(sector.Slice(80, 4), (image.Length / SectorSize));
        BinaryPrimitives.WriteInt32BigEndian(sector.Slice(84, 4), (image.Length / SectorSize));
        BinaryPrimitives.WriteUInt16LittleEndian(sector.Slice(128, 2), (ushort)SectorSize);
        BinaryPrimitives.WriteUInt16BigEndian(sector.Slice(130, 2), (ushort)SectorSize);
        WriteRootDirectoryRecord(sector.Slice(156, 34), rootLba, SectorSize);
    }

    private void WriteJolietVolumeDescriptor(byte[] image, int lba, int rootLba)
    {
        Span<byte> sector = image.AsSpan(lba * SectorSize, SectorSize);
        sector[0] = 0x02; // type: SVD
        WriteAscii(sector.Slice(1, 5), "CD001");
        sector[6] = 0x01; // version
        // Escape sequence at offset 88, level 1 Joliet (UCS-2 level 1): 0x25 0x2F 0x40
        sector[88] = 0x25;
        sector[89] = 0x2F;
        sector[90] = 0x40;
        WriteUcs2BigEndianPadded(sector.Slice(40, 32), _jolietVolumeId!);
        BinaryPrimitives.WriteInt32LittleEndian(sector.Slice(80, 4), (image.Length / SectorSize));
        BinaryPrimitives.WriteInt32BigEndian(sector.Slice(84, 4), (image.Length / SectorSize));
        BinaryPrimitives.WriteUInt16LittleEndian(sector.Slice(128, 2), (ushort)SectorSize);
        BinaryPrimitives.WriteUInt16BigEndian(sector.Slice(130, 2), (ushort)SectorSize);
        WriteRootDirectoryRecord(sector.Slice(156, 34), rootLba, SectorSize);
    }

    private static void WriteVolumeDescriptorSetTerminator(byte[] image, int lba)
    {
        Span<byte> sector = image.AsSpan(lba * SectorSize, SectorSize);
        sector[0] = 0xFF;
        WriteAscii(sector.Slice(1, 5), "CD001");
        sector[6] = 0x01;
    }

    private void WritePrimaryRootDirectory(byte[] image, int rootLba, int firstFileLba)
    {
        Span<byte> dir = image.AsSpan(rootLba * SectorSize, SectorSize);
        int offset = 0;
        offset += WriteAsciiDirectoryRecord(dir.Slice(offset), "\0", rootLba, SectorSize, isDirectory: true);
        offset += WriteAsciiDirectoryRecord(dir.Slice(offset), "\u0001", rootLba, SectorSize, isDirectory: true);
        for (int i = 0; i < _files.Count; i++)
        {
            FileEntry entry = _files[i];
            int fileLba = firstFileLba + i;
            string nameOnDisc = entry.PrimaryName + ";1";
            offset += WriteAsciiDirectoryRecord(dir.Slice(offset), nameOnDisc, fileLba, entry.Contents.Length, isDirectory: false);
        }
    }

    private void WriteJolietRootDirectory(byte[] image, int rootLba, int firstFileLba)
    {
        Span<byte> dir = image.AsSpan(rootLba * SectorSize, SectorSize);
        int offset = 0;
        offset += WriteAsciiDirectoryRecord(dir.Slice(offset), "\0", rootLba, SectorSize, isDirectory: true);
        offset += WriteAsciiDirectoryRecord(dir.Slice(offset), "\u0001", rootLba, SectorSize, isDirectory: true);
        for (int i = 0; i < _files.Count; i++)
        {
            FileEntry entry = _files[i];
            int fileLba = firstFileLba + i;
            offset += WriteUcs2DirectoryRecord(dir.Slice(offset), entry.JolietName, fileLba, entry.Contents.Length, isDirectory: false);
        }
    }

    private static int WriteAsciiDirectoryRecord(Span<byte> destination, string nameAscii, int extentLba, int dataLength, bool isDirectory)
    {
        byte[] nameBytes = Encoding.ASCII.GetBytes(nameAscii);
        return WriteDirectoryRecordCore(destination, nameBytes, extentLba, dataLength, isDirectory);
    }

    private static int WriteUcs2DirectoryRecord(Span<byte> destination, string nameUcs2BigEndian, int extentLba, int dataLength, bool isDirectory)
    {
        byte[] nameBytes = EncodeUcs2BigEndian(nameUcs2BigEndian);
        return WriteDirectoryRecordCore(destination, nameBytes, extentLba, dataLength, isDirectory);
    }

    private static int WriteDirectoryRecordCore(Span<byte> destination, byte[] nameBytes, int extentLba, int dataLength, bool isDirectory)
    {
        int nameLength = nameBytes.Length;
        int recordLength = 33 + nameLength;
        if ((recordLength & 1) != 0)
        {
            recordLength++;
        }
        destination[0] = (byte)recordLength;
        destination[1] = 0;
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(2, 4), extentLba);
        BinaryPrimitives.WriteInt32BigEndian(destination.Slice(6, 4), extentLba);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(10, 4), dataLength);
        BinaryPrimitives.WriteInt32BigEndian(destination.Slice(14, 4), dataLength);
        // Skip date/time fields 18..24 (zero is fine for tests).
        destination[25] = isDirectory ? (byte)0x02 : (byte)0x00;
        destination[26] = 0;
        destination[27] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(28, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(30, 2), 1);
        destination[32] = (byte)nameLength;
        nameBytes.AsSpan().CopyTo(destination.Slice(33, nameLength));
        return recordLength;
    }

    private static void WriteRootDirectoryRecord(Span<byte> destination, int rootLba, int dataLength)
    {
        destination[0] = 34;
        destination[1] = 0;
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(2, 4), rootLba);
        BinaryPrimitives.WriteInt32BigEndian(destination.Slice(6, 4), rootLba);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(10, 4), dataLength);
        BinaryPrimitives.WriteInt32BigEndian(destination.Slice(14, 4), dataLength);
        destination[25] = 0x02; // directory
        destination[28] = 1;
        destination[31] = 1;
        destination[32] = 1;
        destination[33] = 0; // root self-name
    }

    private static void WriteAscii(Span<byte> destination, string text)
    {
        Encoding.ASCII.GetBytes(text, destination);
    }

    private static void WriteAsciiPadded(Span<byte> destination, string text, byte padding)
    {
        destination.Fill(padding);
        int written = Encoding.ASCII.GetBytes(text, destination);
        if (written > destination.Length)
        {
            throw new ArgumentException("Text exceeds destination size", nameof(text));
        }
    }

    private static void WriteUcs2BigEndianPadded(Span<byte> destination, string text)
    {
        destination.Clear();
        byte[] encoded = EncodeUcs2BigEndian(text);
        encoded.AsSpan(0, Math.Min(encoded.Length, destination.Length)).CopyTo(destination);
        // Pad remaining bytes with UCS-2 BE spaces (0x00 0x20).
        for (int i = encoded.Length; i + 1 < destination.Length; i += 2)
        {
            destination[i] = 0x00;
            destination[i + 1] = 0x20;
        }
    }

    private static byte[] EncodeUcs2BigEndian(string text)
    {
        byte[] result = new byte[text.Length * 2];
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            result[i * 2] = (byte)(ch >> 8);
            result[i * 2 + 1] = (byte)(ch & 0xFF);
        }
        return result;
    }

    private sealed record FileEntry(string PrimaryName, string JolietName, byte[] Contents);
}
