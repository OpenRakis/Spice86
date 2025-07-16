namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure.Array;

using System.ComponentModel.DataAnnotations;

public class DosFileControlBlock : MemoryBasedDataStructure {

    public DosFileControlBlock(IByteReaderWriter byteReaderWriter, uint baseAddress) : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// Gets or sets the drive letter. 0=default drive (when unopened, or A), 1=drive A (when unopened, or B), 2=drive B (when unopened, or C), etc.
    /// </summary>
    public byte Drive { get => UInt8[0x0]; set => UInt8[0x0] = value; }

    /// <summary>
    /// Space padded name of the file
    /// </summary>
    [Range(0, 8)]
    public string FileName { get => GetZeroTerminatedString(0x1, 9); set => SetZeroTerminatedString(0x1, value, 9); }

    /// <summary>
    /// Space padded extension of the file
    /// </summary>
    [Range(0, 3)]
    public string Extension { get => GetZeroTerminatedString(0x9, 4); set => SetZeroTerminatedString(0x9, value, 4); }

    public ushort CurrentBlock { get => UInt16[0x0c]; set => UInt16[0x0c] = value; }

    public ushort LogicalRecordSize { get => UInt16[0x0e]; set => UInt16[0x0e] = value; }

    public uint FileSize { get => UInt32[0x10]; set => UInt32[0x10] = value; }

    public ushort Date { get => UInt16[0x14]; set => UInt16[0x14] = value; }

    public ushort Time { get => UInt16[0x16]; set => UInt16[0x16] = value; }

    public byte SftEntries { get => UInt8[0x18]; set => UInt8[0x18] = value; }

    public byte ShareAttributes { get => UInt8[0x19]; set => UInt8[0x19] = value; }

    public byte ExtraInfo { get => UInt8[0x1A]; set => UInt8[0x1A] = value; }

    public byte FileHandle { get => UInt8[0x1B]; set => UInt8[0x1B] = value; }

    /// <summary>
    /// Reserved, undocumented
    /// </summary>
    public UInt8Array Reserved => GetUInt8Array(0x1C, 8);

    public ushort CurrentRecord { get => UInt16[0x20]; set => UInt16[0x20] = value; }

    public uint CurrentRecordNumber { get => UInt32[0x22]; set => UInt32[0x22] = value; }

    /// <summary>
    /// Checks if the FCB is valid.
    /// </summary>
    /// <returns>true if the FCB is valid, false otherwise.</returns>
    public bool Valid() {
        // Simple check for filename or file handle (from DOSBox for Oubliette)
        return UInt8[0x1] != 0 || FileHandle != 0xFF;
    }

    /// <summary>
    /// Gets the sequential data from the FCB.
    /// </summary>
    /// <param name="fhandle">Output parameter for file handle.</param>
    /// <param name="rsize">Output parameter for record size.</param>
    public void GetSeqData(out byte fhandle, out ushort rsize) {
        fhandle = FileHandle;
        rsize = LogicalRecordSize;
    }

    /// <summary>
    /// Sets the sequential data in the FCB.
    /// </summary>
    /// <param name="fhandle">File handle to set.</param>
    /// <param name="rsize">Record size to set.</param>
    public void SetSeqData(byte fhandle, ushort rsize) {
        FileHandle = fhandle;
        LogicalRecordSize = rsize;
    }

    /// <summary>
    /// Gets the current record position from the FCB.
    /// </summary>
    /// <param name="block">Output parameter for current block.</param>
    /// <param name="rec">Output parameter for current record.</param>
    public void GetRecord(out ushort block, out byte rec) {
        block = CurrentBlock;
        rec = (byte)CurrentRecord;
    }

    /// <summary>
    /// Sets the current record position in the FCB.
    /// </summary>
    /// <param name="block">Current block to set.</param>
    /// <param name="rec">Current record to set.</param>
    public void SetRecord(ushort block, byte rec) {
        CurrentBlock = block;
        CurrentRecord = rec;
    }

    /// <summary>
    /// Gets the size, date, and time from the FCB.
    /// </summary>
    /// <param name="size">Output parameter for file size.</param>
    /// <param name="date">Output parameter for file date.</param>
    /// <param name="time">Output parameter for file time.</param>
    public void GetSizeDateTime(out uint size, out ushort date, out ushort time) {
        size = FileSize;
        date = Date;
        time = Time;
    }

    /// <summary>
    /// Sets the size, date, and time in the FCB.
    /// </summary>
    /// <param name="size">File size to set.</param>
    /// <param name="date">File date to set.</param>
    /// <param name="time">File time to set.</param>
    public void SetSizeDateTime(uint size, ushort date, ushort time) {
        FileSize = size;
        Date = date;
        Time = time;
    }

    /// <summary>
    /// Gets the random record number from the FCB.
    /// </summary>
    /// <returns>The random record number.</returns>
    public uint GetRandom() {
        return CurrentRecordNumber;
    }

    /// <summary>
    /// Sets the random record number in the FCB.
    /// </summary>
    /// <param name="random">Random record number to set.</param>
    public void SetRandom(uint random) {
        CurrentRecordNumber = random;
    }

    /// <summary>
    /// Creates a new FCB.
    /// </summary>
    /// <param name="extended">Whether to create an extended FCB.</param>
    public void Create(bool extended) {
        // Clear FCB data
        int fillSize = extended ? 33 + 7 : 33;
        for (int i = 0; i < fillSize; i++) {
            UInt8[i] = 0;
        }
        
        // Mark as extended FCB if requested
        if (extended) {
            UInt8[0] = DosExtendedFileControlBlock.ExpectedSignature;
        }
    }

    /// <summary>
    /// Sets the FCB name.
    /// </summary>
    /// <param name="drive">Drive number.</param>
    /// <param name="name">Filename (8 characters).</param>
    /// <param name="ext">Extension (3 characters).</param>
    public void SetName(byte drive, string name, string ext) {
        Drive = drive;
        FileName = name;
        Extension = ext;
    }

    /// <summary>
    /// Gets attributes from the FCB if it's extended.
    /// </summary>
    /// <param name="attr">Output parameter for attributes.</param>
    public void GetAttr(out DosFileAttributes attr) {
        attr = DosFileAttributes.Normal;
        if (Drive == DosExtendedFileControlBlock.ExpectedSignature) {
            DosExtendedFileControlBlock extFcb = new(this);
            attr = (DosFileAttributes)extFcb.FileAttribute;
        }
    }

    /// <summary>
    /// Sets attributes in the FCB if it's extended.
    /// </summary>
    /// <param name="attr">Attributes to set.</param>
    public void SetAttr(DosFileAttributes attr) {
        if (Drive == DosExtendedFileControlBlock.ExpectedSignature) {
            DosExtendedFileControlBlock extFcb = new(this);
            extFcb.FileAttribute = (byte)attr;
        }
    }

    /// <summary>
    /// Sets file result data in the FCB.
    /// </summary>
    /// <param name="size">File size.</param>
    /// <param name="date">File date.</param>
    /// <param name="time">File time.</param>
    /// <param name="attr">File attributes.</param>
    public void SetResult(uint size, ushort date, ushort time, DosFileAttributes attr) {
        FileSize = size;
        Date = date;
        Time = time;
        
        // Store attributes in the FCB (undocumented field in classic FCBs)
        UInt8[0x0C] = (byte)attr;
    }
}