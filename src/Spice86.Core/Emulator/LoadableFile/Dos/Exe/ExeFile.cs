namespace Spice86.Core.Emulator.LoadableFile.Dos.Exe;

using System.Text;

using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

/// <summary>
/// Represents an executable file in the DOS MZ format.
/// </summary>
public class ExeFile {
    /// <summary>
    /// Initializes a new instance of the ExeFile class using the specified byte array containing the contents of the executable file.
    /// </summary>
    /// <param name="exe">The byte array containing the contents of the executable file.</param>
    public ExeFile(byte[] exe) {
        Signature = new string(Encoding.UTF8.GetChars(exe), 0, 2);
        ExtraBytes = MemoryUtils.GetUint16(exe, 0x02);
        Pages = MemoryUtils.GetUint16(exe, 0x04);
        RelocItems = MemoryUtils.GetUint16(exe, 0x06);
        HeaderSize = MemoryUtils.GetUint16(exe, 0x08);
        MinAlloc = MemoryUtils.GetUint16(exe, 0x0A);
        MaxAlloc = MemoryUtils.GetUint16(exe, 0x0C);
        InitSS = MemoryUtils.GetUint16(exe, 0x0E);
        InitSP = MemoryUtils.GetUint16(exe, 0x10);
        CheckSum = MemoryUtils.GetUint16(exe, 0x12);
        InitIP = MemoryUtils.GetUint16(exe, 0x14);
        InitCS = MemoryUtils.GetUint16(exe, 0x16);
        RelocTable = MemoryUtils.GetUint16(exe, 0x18);
        Overlay = MemoryUtils.GetUint16(exe, 0x1A);
        int relocationTableOffset = RelocTable;
        int numRelocationEntries = RelocItems;
        for (int i = 0; i < numRelocationEntries; i++) {
            uint currentEntry = (uint)(relocationTableOffset + (i * 4));
            ushort offset = MemoryUtils.GetUint16(exe, currentEntry);
            ushort segment = MemoryUtils.GetUint16(exe, currentEntry + 2);
            RelocationTable.Add(new SegmentedAddress(segment, offset));
        }

        int actualHeaderSize = HeaderSize * 16;
        int programSize = exe.Length - actualHeaderSize;
        ProgramImage = new byte[programSize];
        Array.Copy(exe, actualHeaderSize, ProgramImage, 0, programSize);
    }

    /// <summary>
    /// Gets the checksum of the executable file.
    /// </summary>
    public ushort CheckSum { get; private set; }

    /// <summary>
    /// Gets the size of the program code in the executable file.
    /// </summary>
    public int CodeSize => ProgramImage.Length;

    /// <summary>
    /// Gets the number of extra bytes needed by the program.
    /// </summary>
    public ushort ExtraBytes { get; private set; }

    /// <summary>
    /// Gets the size of the header in paragraphs.
    /// </summary>
    public ushort HeaderSize { get; private set; }

    /// <summary>
    /// Gets the initial (relative) CS value of the program.
    /// </summary>
    public ushort InitCS { get; private set; }

    /// <summary>
    /// Gets the initial IP value of the program.
    /// </summary>
    public ushort InitIP { get; private set; }

    /// <summary>
    /// Gets the initial SP value of the program.
    /// </summary>
    public ushort InitSP { get; private set; }

    /// <summary>
    /// Gets the initial (relative) SS value of the program.
    /// </summary>
    public ushort InitSS { get; private set; }

    /// <summary>
    /// Gets the maximum number of extra paragraphs needed by the program.
    /// </summary>
    public ushort MaxAlloc { get; private set; }

    /// <summary>
    /// Gets the minimum number of extra paragraphs needed by the program.
    /// </summary>
    public ushort MinAlloc { get; private set; }

    /// <summary>
    /// Gets the overlay number of the program.
    /// </summary>
    public ushort Overlay { get; private set; }

    /// <summary>
    /// Gets the number of pages in the executable file.
    /// </summary>
    public ushort Pages { get; private set; }

    /// <summary>
    /// Gets the program code in the executable file.
    /// </summary>
    public byte[] ProgramImage { get; private set; }

    /// <summary>
    /// Gets the list of relocation table entries in the executable file.
    /// </summary>
    public IList<SegmentedAddress> RelocationTable { get; private set; } = new List<SegmentedAddress>();

    /// <summary>
    /// Gets the number of relocation table entries in the executable file.
    /// </summary>
    public ushort RelocItems { get; private set; }

    /// <summary>
    /// Gets the offset of the relocation table in the executable file.
    /// </summary>
    public ushort RelocTable { get; private set; }

    /// <summary>
    /// Gets the signature of the executable file.
    /// </summary>
    public string Signature { get; private set; }

    /// <summary>
    /// Returns a JSON representation of the ExeFile object.
    /// </summary>
    /// <returns>A JSON representation of the ExeFile object.</returns>
    public override string ToString() {
        return System.Text.Json.JsonSerializer.Serialize(this);
    }
}