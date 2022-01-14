namespace Spice86.Emulator.Loadablefile.Dos.Exe;

using Spice86.Emulator.Memory;

using System.Collections.Generic;
using System.Text;

public class ExeFile
{
    private int checkSum;
    private int extraBytes;
    private int headerSize;
    private int initCS;

    // 0012 - Checksum
    private int initIP;

    private int initSP;
    private int initSS;
    private int maxAlloc;

    // 0008 - Size of header in paragraphs
    private int minAlloc;

    private int overlay;

    // 0002 - Bytes on last page of file
    private int pages;

    private byte[] programImage;

    // 001A - Overlay number
    private List<SegmentedAddress> relocationTable = new();

    // 0004 - Pages in file
    private int relocItems;

    // 0006 - Relocations
    // 000A - Minimum extra paragraphs needed
    // 000C - Maximum extra paragraphs needed
    // 000E - Initial (relative) SS value
    // 0010 - Initial SP value
    // 0014 - Initial IP value
    // 0016 - Initial (relative) CS value
    private int relocTable;

    private string signature; // 0000 - Magic number

    // 0018 - File address of relocation table
    public ExeFile(byte[] exe)
    {
        this.signature = new string(Encoding.UTF8.GetChars(exe), 0, 2);
        this.extraBytes = MemoryUtils.GetUint16(exe, 0x02);
        this.pages = MemoryUtils.GetUint16(exe, 0x04);
        this.relocItems = MemoryUtils.GetUint16(exe, 0x06);
        this.headerSize = MemoryUtils.GetUint16(exe, 0x08);
        this.minAlloc = MemoryUtils.GetUint16(exe, 0x0A);
        this.maxAlloc = MemoryUtils.GetUint16(exe, 0x0C);
        this.initSS = MemoryUtils.GetUint16(exe, 0x0E);
        this.initSP = MemoryUtils.GetUint16(exe, 0x10);
        this.checkSum = MemoryUtils.GetUint16(exe, 0x12);
        this.initIP = MemoryUtils.GetUint16(exe, 0x14);
        this.initCS = MemoryUtils.GetUint16(exe, 0x16);
        this.relocTable = MemoryUtils.GetUint16(exe, 0x18);
        this.overlay = MemoryUtils.GetUint16(exe, 0x1A);
        int relocationTableOffset = this.relocTable;
        int numRelocationEntries = this.relocItems;
        for (int i = 0; i < numRelocationEntries; i++)
        {
            int offset = MemoryUtils.GetUint16(exe, relocationTableOffset + i * 4);
            int segment = MemoryUtils.GetUint16(exe, relocationTableOffset + i * 4 + 2);
            relocationTable.Add(new SegmentedAddress(segment, offset));
        }

        int actualHeaderSize = headerSize * 16;
        int programSize = exe.Length - actualHeaderSize;
        programImage = new byte[programSize];
        System.Array.Copy(exe, actualHeaderSize, programImage, 0, programSize);
    }

    public int GetCheckSum()
    {
        return checkSum;
    }

    public int GetCodeSize()
    {
        return programImage.Length;
    }

    public int GetExtraBytes()
    {
        return extraBytes;
    }

    public int GetHeaderSize()
    {
        return headerSize;
    }

    public int GetInitCS()
    {
        return initCS;
    }

    public int GetInitIP()
    {
        return initIP;
    }

    public int GetInitSP()
    {
        return initSP;
    }

    public int GetInitSS()
    {
        return initSS;
    }

    public int GetMaxAlloc()
    {
        return maxAlloc;
    }

    public int GetMinAlloc()
    {
        return minAlloc;
    }

    public int GetOverlay()
    {
        return overlay;
    }

    public int GetPages()
    {
        return pages;
    }

    public byte[] GetProgramImage()
    {
        return programImage;
    }

    public IList<SegmentedAddress> GetRelocationTable()
    {
        return relocationTable;
    }

    public int GetRelocItems()
    {
        return relocItems;
    }

    public int GetRelocTable()
    {
        return relocTable;
    }

    public string GetSignature()
    {
        return signature;
    }

    public void SetCheckSum(int checkSum)
    {
        this.checkSum = checkSum;
    }

    public void SetExtraBytes(int extraBytes)
    {
        this.extraBytes = extraBytes;
    }

    public void SetHeaderSize(int headerSize)
    {
        this.headerSize = headerSize;
    }

    public void SetInitCS(int initCS)
    {
        this.initCS = initCS;
    }

    public void SetInitIP(int initIP)
    {
        this.initIP = initIP;
    }

    public void SetInitSP(int initSP)
    {
        this.initSP = initSP;
    }

    public void SetInitSS(int initSS)
    {
        this.initSS = initSS;
    }

    public void SetMaxAlloc(int maxAlloc)
    {
        this.maxAlloc = maxAlloc;
    }

    public void SetMinAlloc(int minAlloc)
    {
        this.minAlloc = minAlloc;
    }

    public void SetOverlay(int overlay)
    {
        this.overlay = overlay;
    }

    public void SetPages(int pages)
    {
        this.pages = pages;
    }

    public void SetProgramImage(byte[] programImage)
    {
        this.programImage = programImage;
    }

    public void SetRelocationTable(List<SegmentedAddress> relocationTable)
    {
        this.relocationTable = relocationTable;
    }

    public void SetRelocItems(int relocItems)
    {
        this.relocItems = relocItems;
    }

    public void SetRelocTable(int relocTable)
    {
        this.relocTable = relocTable;
    }

    public void SetSignature(string signature)
    {
        this.signature = signature;
    }

    public override string ToString()
    {
        return System.Text.Json.JsonSerializer.Serialize(this);
    }
}