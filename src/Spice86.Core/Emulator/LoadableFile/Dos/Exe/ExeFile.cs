namespace Spice86.Core.Emulator.LoadableFile.Dos.Exe;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Representation of an EXE file as it is stored on disk, loaded into memory.
/// </summary>
public class ExeFile : MemoryBasedDataStructure {
    /// <summary>
    /// Creates a new instance of the ExeFile class.
    /// </summary>
    /// <param name="byteReaderWriter">The class that allows writing and reading at specific addresses in memory.</param>
    public ExeFile(IByteReaderWriter byteReaderWriter) : base(byteReaderWriter, 0) {
    }

    /// <summary>
    /// Signature of the executable file.
    /// </summary>
    public string Signature => GetZeroTerminatedString(0, 2);

    /// <summary>
    /// Number of extra bytes needed by the program.
    /// </summary>
    public ushort ExtraBytes => UInt16[0x02];

    /// <summary>
    /// Number of pages in the executable file.
    /// </summary>
    public ushort Pages => UInt16[0x04];

    /// <summary>
    /// Number of relocation table entries in the executable file.
    /// </summary>
    public ushort RelocItems => UInt16[0x06];

    /// <summary>
    /// Size of the header in paragraphs.
    /// </summary>
    public ushort HeaderSizeInParagraphs => UInt16[0x08];

    /// <summary>
    /// Minimum number of extra paragraphs needed by the program.
    /// </summary>
    public ushort MinAlloc => UInt16[0x0A];

    /// <summary>
    /// Maximum number of extra paragraphs needed by the program.
    /// </summary>
    public ushort MaxAlloc => UInt16[0x0C];

    /// <summary>
    /// Initial (relative) SS value of the program.
    /// </summary>
    public ushort InitSS => UInt16[0x0E];

    /// <summary>
    /// Initial SP value of the program.
    /// </summary>
    public ushort InitSP => UInt16[0x10];

    /// <summary>
    /// Checksum of the executable file.
    /// </summary>
    public ushort CheckSum => UInt16[0x12];

    /// <summary>
    /// Initial IP value of the program.
    /// </summary>
    public ushort InitIP => UInt16[0x14];

    /// <summary>
    /// Initial (relative) CS value of the program.
    /// </summary>
    public ushort InitCS => UInt16[0x16];

    /// <summary>
    /// Offset of the relocation table in the executable file.
    /// </summary>
    public ushort RelocTableOffset => UInt16[0x18];

    /// <summary>
    /// Overlay number of the program.
    /// </summary>
    public ushort Overlay => UInt16[0x1A];

    /// <summary>
    /// Enumeration of relocation table entries in the executable file.
    /// </summary>
    public IEnumerable<SegmentedAddress> RelocationTable => GetSegmentedAddressArray(RelocTableOffset, RelocItems);

    /// <summary>
    /// Program code in the executable file.
    /// </summary>
    public byte[] ProgramImage {
        get {
            byte[] res = new byte[ProgramSize];
            for (int i = 0; i < ProgramSize; i++) {
                res[i] = UInt8[(uint)(i + HeaderSizeInBytes)];
            }
            return res;
        }
    }

    /// <summary>
    /// Size of header in bytes
    /// </summary>
    public uint HeaderSizeInBytes => (uint)(HeaderSizeInParagraphs * 16);

    /// <summary>
    /// Size of the program code in the executable file.
    /// </summary>
    public uint ProgramSize => ByteReaderWriter.Length - HeaderSizeInBytes;

    /// <summary>
    /// True when represented EXE is valid.
    /// Valid means starts with "MZ" and declared header is at least as big as the whole exe.
    /// </summary>
    public bool IsValid => Signature is "MZ" or "ZM" && HeaderSizeInBytes <= ByteReaderWriter.Length;
}