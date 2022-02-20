namespace Spice86.Emulator.Loadablefile.Dos.Exe;

using Spice86.Emulator.Memory;

using System;
using System.Collections.Generic;
using System.Text;

public class ExeFile {

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
            uint currentEntry = (uint)(relocationTableOffset + i * 4);
            ushort offset = MemoryUtils.GetUint16(exe, currentEntry);
            ushort segment = MemoryUtils.GetUint16(exe, currentEntry + 2);
            RelocationTable.Add(new SegmentedAddress(segment, offset));
        }

        int actualHeaderSize = HeaderSize * 16;
        int programSize = exe.Length - actualHeaderSize;
        ProgramImage = new byte[programSize];
        Array.Copy(exe, actualHeaderSize, ProgramImage, 0, programSize);
    }

    /// <summary> 0012 - Checksum </summary>
    public ushort CheckSum { get; private set; }

    public int CodeSize => ProgramImage.Length;

    /// <summary> 000A - Minimum extra paragraphs needed </summary>
    public ushort ExtraBytes { get; private set; }

    public ushort HeaderSize { get; private set; }

    /// <summary> 0016 - Initial (relative) CS value </summary>
    public ushort InitCS { get; private set; }

    /// <summary> 0014 - Initial IP value </summary>
    public ushort InitIP { get; private set; }

    /// <summary> 0010 - Initial SP value </summary>
    public ushort InitSP { get; private set; }

    /// <summary> 000E - Initial (relative) SS value </summary>
    public ushort InitSS { get; private set; }

    public ushort MaxAlloc { get; private set; }

    /// <summary> 0008 - Size of header in paragraphs</summary>
    public ushort MinAlloc { get; private set; }

    public ushort Overlay { get; private set; }

    /// <summary> 000C - Maximum extra paragraphs needed </summary>
    public ushort Pages { get; private set; }

    /// <summary> 0002 - Bytes on last page of file </summary>
    public byte[] ProgramImage { get; private set; }

    public IList<SegmentedAddress> RelocationTable { get; private set; } = new List<SegmentedAddress>();

    public ushort RelocItems { get; private set; }

    /// <summary> 0006 - Relocations </summary>
    public ushort RelocTable { get; private set; }

    public string Signature { get; private set; }

    public override string ToString() {
        return System.Text.Json.JsonSerializer.Serialize(this);
    }
}