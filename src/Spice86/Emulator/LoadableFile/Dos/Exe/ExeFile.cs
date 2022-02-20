namespace Spice86.Emulator.Loadablefile.Dos.Exe;

using Spice86.Emulator.Memory;

using System.Collections.Generic;
using System.Text;

public class ExeFile {
    private ushort _checkSum;
    private ushort _extraBytes;
    private ushort _headerSize;
    private ushort _initCS;

    // 0012 - Checksum
    private ushort _initIP;

    private ushort _initSP;
    private ushort _initSS;
    private ushort _maxAlloc;

    // 0008 - Size of header in paragraphs
    private ushort _minAlloc;

    private ushort _overlay;

    // 0002 - Bytes on last page of file
    private ushort _pages;

    private byte[] _programImage;

    // 001A - Overlay number
    private List<SegmentedAddress> _relocationTable = new();

    // 0004 - Pages in file
    private ushort _relocItems;

    // 0006 - Relocations
    // 000A - Minimum extra paragraphs needed
    // 000C - Maximum extra paragraphs needed
    // 000E - Initial (relative) SS value
    // 0010 - Initial SP value
    // 0014 - Initial IP value
    // 0016 - Initial (relative) CS value
    private ushort _relocTable;

    private string _signature; // 0000 - Magic number

    // 0018 - File address of relocation table
    public ExeFile(byte[] exe) {
        this._signature = new string(Encoding.UTF8.GetChars(exe), 0, 2);
        this._extraBytes = MemoryUtils.GetUint16(exe, 0x02);
        this._pages = MemoryUtils.GetUint16(exe, 0x04);
        this._relocItems = MemoryUtils.GetUint16(exe, 0x06);
        this._headerSize = MemoryUtils.GetUint16(exe, 0x08);
        this._minAlloc = MemoryUtils.GetUint16(exe, 0x0A);
        this._maxAlloc = MemoryUtils.GetUint16(exe, 0x0C);
        this._initSS = MemoryUtils.GetUint16(exe, 0x0E);
        this._initSP = MemoryUtils.GetUint16(exe, 0x10);
        this._checkSum = MemoryUtils.GetUint16(exe, 0x12);
        this._initIP = MemoryUtils.GetUint16(exe, 0x14);
        this._initCS = MemoryUtils.GetUint16(exe, 0x16);
        this._relocTable = MemoryUtils.GetUint16(exe, 0x18);
        this._overlay = MemoryUtils.GetUint16(exe, 0x1A);
        int relocationTableOffset = this._relocTable;
        int numRelocationEntries = this._relocItems;
        for (int i = 0; i < numRelocationEntries; i++) {
            uint currentEntry = (uint)(relocationTableOffset + i * 4);
            ushort offset = MemoryUtils.GetUint16(exe, currentEntry);
            ushort segment = MemoryUtils.GetUint16(exe, currentEntry + 2);
            _relocationTable.Add(new SegmentedAddress(segment, offset));
        }

        int actualHeaderSize = _headerSize * 16;
        int programSize = exe.Length - actualHeaderSize;
        _programImage = new byte[programSize];
        System.Array.Copy(exe, actualHeaderSize, _programImage, 0, programSize);
    }

    public ushort GetCheckSum() {
        return _checkSum;
    }

    public int GetCodeSize() {
        return _programImage.Length;
    }

    public ushort GetExtraBytes() {
        return _extraBytes;
    }

    public ushort GetHeaderSize() {
        return _headerSize;
    }

    public ushort GetInitCS() {
        return _initCS;
    }

    public ushort GetInitIP() {
        return _initIP;
    }

    public ushort GetInitSP() {
        return _initSP;
    }

    public ushort GetInitSS() {
        return _initSS;
    }

    public ushort GetMaxAlloc() {
        return _maxAlloc;
    }

    public ushort GetMinAlloc() {
        return _minAlloc;
    }

    public ushort GetOverlay() {
        return _overlay;
    }

    public ushort GetPages() {
        return _pages;
    }

    public byte[] GetProgramImage() {
        return _programImage;
    }

    public IList<SegmentedAddress> GetRelocationTable() {
        return _relocationTable;
    }

    public ushort GetRelocItems() {
        return _relocItems;
    }

    public ushort GetRelocTable() {
        return _relocTable;
    }

    public string GetSignature() {
        return _signature;
    }

    public void SetCheckSum(ushort checkSum) {
        this._checkSum = checkSum;
    }

    public void SetExtraBytes(ushort extraBytes) {
        this._extraBytes = extraBytes;
    }

    public void SetHeaderSize(ushort headerSize) {
        this._headerSize = headerSize;
    }

    public void SetInitCS(ushort initCS) {
        this._initCS = initCS;
    }

    public void SetInitIP(ushort initIP) {
        this._initIP = initIP;
    }

    public void SetInitSP(ushort initSP) {
        this._initSP = initSP;
    }

    public void SetInitSS(ushort initSS) {
        this._initSS = initSS;
    }

    public void SetMaxAlloc(ushort maxAlloc) {
        this._maxAlloc = maxAlloc;
    }

    public void SetMinAlloc(ushort minAlloc) {
        this._minAlloc = minAlloc;
    }

    public void SetOverlay(ushort overlay) {
        this._overlay = overlay;
    }

    public void SetPages(ushort pages) {
        this._pages = pages;
    }

    public void SetProgramImage(byte[] programImage) {
        this._programImage = programImage;
    }

    public void SetRelocationTable(List<SegmentedAddress> relocationTable) {
        this._relocationTable = relocationTable;
    }

    public void SetRelocItems(ushort relocItems) {
        this._relocItems = relocItems;
    }

    public void SetRelocTable(ushort relocTable) {
        this._relocTable = relocTable;
    }

    public void SetSignature(string signature) {
        this._signature = signature;
    }

    public override string ToString() {
        return System.Text.Json.JsonSerializer.Serialize(this);
    }
}