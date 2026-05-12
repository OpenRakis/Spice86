using System.Text;

namespace Spice86.Core.Emulator.Devices.CdRom.Image;

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
}
