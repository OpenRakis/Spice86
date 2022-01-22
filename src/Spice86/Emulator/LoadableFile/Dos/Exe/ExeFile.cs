namespace Spice86.Emulator.Loadablefile.Dos.Exe;

using Spice86.Emulator.Memory;

using System.Collections.Generic;
using System.Text;

public class ExeFile {
    private ushort checkSum;
    private ushort extraBytes;
    private ushort headerSize;
    private ushort initCS;

    // 0012 - Checksum
    private ushort initIP;

    private ushort initSP;
    private ushort initSS;
    private ushort maxAlloc;

    // 0008 - Size of header in paragraphs
    private ushort minAlloc;

    private ushort overlay;

    // 0002 - Bytes on last page of file
    private ushort pages;

    private byte[] programImage;

    // 001A - Overlay number
    private List<SegmentedAddress> relocationTable = new();

    // 0004 - Pages in file
    private ushort relocItems;

    // 0006 - Relocations
    // 000A - Minimum extra paragraphs needed
    // 000C - Maximum extra paragraphs needed
    // 000E - Initial (relative) SS value
    // 0010 - Initial SP value
    // 0014 - Initial IP value
    // 0016 - Initial (relative) CS value
    private ushort relocTable;

    private string signature; // 0000 - Magic number

    // 0018 - File address of relocation table
    public ExeFile(byte[] exe) {
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
        for (int i = 0; i < numRelocationEntries; i++) {
            uint currentEntry = (uint)(relocationTableOffset + i * 4);
            ushort offset = MemoryUtils.GetUint16(exe, currentEntry);
            ushort segment = MemoryUtils.GetUint16(exe, currentEntry + 2);
            relocationTable.Add(new SegmentedAddress(segment, offset));
        }

        int actualHeaderSize = headerSize * 16;
        int programSize = exe.Length - actualHeaderSize;
        programImage = new byte[programSize];
        System.Array.Copy(exe, actualHeaderSize, programImage, 0, programSize);
    }

    public ushort GetCheckSum() {
        return checkSum;
    }

    public int GetCodeSize() {
        return programImage.Length;
    }

    public ushort GetExtraBytes() {
        return extraBytes;
    }

    public ushort GetHeaderSize() {
        return headerSize;
    }

    public ushort GetInitCS() {
        return initCS;
    }

    public ushort GetInitIP() {
        return initIP;
    }

    public ushort GetInitSP() {
        return initSP;
    }

    public ushort GetInitSS() {
        return initSS;
    }

    public ushort GetMaxAlloc() {
        return maxAlloc;
    }

    public ushort GetMinAlloc() {
        return minAlloc;
    }

    public ushort GetOverlay() {
        return overlay;
    }

    public ushort GetPages() {
        return pages;
    }

    public byte[] GetProgramImage() {
        return programImage;
    }

    public IList<SegmentedAddress> GetRelocationTable() {
        return relocationTable;
    }

    public ushort GetRelocItems() {
        return relocItems;
    }

    public ushort GetRelocTable() {
        return relocTable;
    }

    public string GetSignature() {
        return signature;
    }

    public void SetCheckSum(ushort checkSum) {
        this.checkSum = checkSum;
    }

    public void SetExtraBytes(ushort extraBytes) {
        this.extraBytes = extraBytes;
    }

    public void SetHeaderSize(ushort headerSize) {
        this.headerSize = headerSize;
    }

    public void SetInitCS(ushort initCS) {
        this.initCS = initCS;
    }

    public void SetInitIP(ushort initIP) {
        this.initIP = initIP;
    }

    public void SetInitSP(ushort initSP) {
        this.initSP = initSP;
    }

    public void SetInitSS(ushort initSS) {
        this.initSS = initSS;
    }

    public void SetMaxAlloc(ushort maxAlloc) {
        this.maxAlloc = maxAlloc;
    }

    public void SetMinAlloc(ushort minAlloc) {
        this.minAlloc = minAlloc;
    }

    public void SetOverlay(ushort overlay) {
        this.overlay = overlay;
    }

    public void SetPages(ushort pages) {
        this.pages = pages;
    }

    public void SetProgramImage(byte[] programImage) {
        this.programImage = programImage;
    }

    public void SetRelocationTable(List<SegmentedAddress> relocationTable) {
        this.relocationTable = relocationTable;
    }

    public void SetRelocItems(ushort relocItems) {
        this.relocItems = relocItems;
    }

    public void SetRelocTable(ushort relocTable) {
        this.relocTable = relocTable;
    }

    public void SetSignature(string signature) {
        this.signature = signature;
    }

    public override string ToString() {
        return System.Text.Json.JsonSerializer.Serialize(this);
    }
}