using System.Text;

using System.Buffers.Binary;

namespace Spice86.Shared.Emulator.Storage.CdRom;

/// <summary>Represents a single entry in an ISO 9660 directory.</summary>
public sealed class IsoDirectoryRecord {
    /// <summary>Gets the file or directory name with version suffix stripped.</summary>
    public string Name { get; }

    /// <summary>Gets the logical block address of the file data.</summary>
    public int ExtentLba { get; }

    /// <summary>Gets the size of the file data in bytes.</summary>
    public int DataLength { get; }

    /// <summary>Gets a value indicating whether this record describes a directory.</summary>
    public bool IsDirectory { get; }

    /// <summary>Gets the raw ISO 9660 file flags byte.</summary>
    public byte FileFlags { get; }

    /// <summary>Initialises a new <see cref="IsoDirectoryRecord"/>.</summary>
    public IsoDirectoryRecord(string name, int extentLba, int dataLength, bool isDirectory, byte fileFlags) {
        Name = name;
        ExtentLba = extentLba;
        DataLength = dataLength;
        IsDirectory = isDirectory;
        FileFlags = fileFlags;
    }

    /// <summary>
    /// Parses an ISO 9660 directory record from raw bytes.
    /// Returns <see langword="null"/> when the record length byte is zero (end-of-directory sentinel).
    /// </summary>
    /// <param name="data">Raw bytes starting at the first byte of a directory record.</param>
    /// <returns>A parsed record, or <see langword="null"/> if the record length is zero.</returns>
    public static IsoDirectoryRecord? ParseNullable(ReadOnlySpan<byte> data) {
        if (data.Length == 0 || data[0] == 0) {
            return null;
        }
        return Parse(data);
    }

    /// <summary>Parses an ISO 9660 directory record from raw bytes.</summary>
    /// <param name="data">Raw bytes starting at the first byte of a directory record.</param>
    /// <returns>A parsed record.</returns>
    public static IsoDirectoryRecord Parse(ReadOnlySpan<byte> data) {
        int extentLba = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(2, 4));
        int dataLength = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(10, 4));
        byte fileFlags = data[25];
        bool isDirectory = (fileFlags & 0x02) != 0;
        byte nameLength = data[32];
        string rawName = Encoding.ASCII.GetString(data.Slice(33, nameLength));
        string name = StripVersion(rawName).ToUpperInvariant();
        return new IsoDirectoryRecord(name, extentLba, dataLength, isDirectory, fileFlags);
    }

    private static string StripVersion(string name) {
        int semicolon = name.IndexOf(';');
        if (semicolon >= 0) {
            return name.Substring(0, semicolon);
        }
        return name;
    }

    /// <summary>
    /// Parses an ISO 9660 directory record using Joliet (UCS-2 big-endian) name
    /// decoding. Preserves case (Joliet is case-sensitive on disc) and strips
    /// any <c>;N</c> version suffix. Returns <see langword="null"/> when the
    /// record length byte is zero (end-of-directory sentinel).
    /// </summary>
    /// <param name="data">Raw bytes starting at the first byte of a directory record.</param>
    public static IsoDirectoryRecord? ParseJolietNullable(ReadOnlySpan<byte> data) {
        if (data.Length == 0 || data[0] == 0) {
            return null;
        }
        return ParseJoliet(data);
    }

    /// <summary>
    /// Parses an ISO 9660 directory record using Joliet (UCS-2 big-endian) name
    /// decoding. Preserves case (Joliet is case-sensitive on disc) and strips
    /// any <c>;N</c> version suffix.
    /// </summary>
    /// <param name="data">Raw bytes starting at the first byte of a directory record.</param>
    public static IsoDirectoryRecord ParseJoliet(ReadOnlySpan<byte> data) {
        int extentLba = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(2, 4));
        int dataLength = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(10, 4));
        byte fileFlags = data[25];
        bool isDirectory = (fileFlags & 0x02) != 0;
        byte nameLength = data[32];
        string name;
        if (nameLength == 1 && (data[33] == 0x00 || data[33] == 0x01)) {
            name = data[33] == 0x00 ? "." : "..";
        } else {
            string rawName = Encoding.BigEndianUnicode.GetString(data.Slice(33, nameLength));
            name = StripVersion(rawName);
        }
        return new IsoDirectoryRecord(name, extentLba, dataLength, isDirectory, fileFlags);
    }
}
