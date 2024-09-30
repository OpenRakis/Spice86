namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Shared.Utils;

/// <summary>
/// Represents the Program Segment Prefix (PSP)
/// </summary>
public sealed class DosProgramSegmentPrefix : DosEnvironmentBlock {
    public DosProgramSegmentPrefix(IByteReaderWriter byteReaderWriter, uint baseAddress) : base(byteReaderWriter, baseAddress) {
    }

    public void MakeNew(ushort memSize) {

    }

    public void CloseFiles() {

    }

    /// <summary>
    /// Gets the <see cref="BaseAddress"/> of the PSP as a segment.
    /// </summary>
    public ushort Segment => MemoryUtils.ToSegment(BaseAddress);

    public void SaveVectors() {

    }

    public void RestoreVectors() {

    }

    public override string? GetEnvironmentVariable(string variableName) {
        throw new NotImplementedException();
    }

    public override void SetEnvironmentVariable(string variableName, string value) {
        throw new NotImplementedException();
    }

    /// <summary>
    /// CP/M like exit point for INT 0x20. (machine code: 0xCD, 0x20). Old way to exit the program.
    /// </summary>
    public byte[] Exit { get => GetData(0, 2); set { LoadData(0, value); } }

    /// <summary>
    /// Segment of first byte beyond the end of the program image. Reserved.
    /// </summary>
    public ushort NextSegment { get => UInt16[2]; set => UInt16[2] = value; }

    /// <summary>
    /// (reserved)
    /// </summary>
    public byte Reserved { get => UInt8[4]; set => UInt8[4] = value; }

    /// <summary>
    /// Far call to DOS INT 0x21 dispatcher. Obsolete.
    /// </summary>
    public byte FarCall { get => UInt8[5]; set => UInt8[5] = value; }

    /// <summary>
    /// On exit, DOS copies this to the INT 0x22 vector.
    /// </summary>
    public uint TerminateAddress { get => UInt32[6]; set => UInt32[6] = value; }

    /// <summary>
    /// On exit, DOS copies this to the INT 0x23 vector.
    /// </summary>
    public uint BreakAddress { get => UInt32[10]; set => UInt32[10] = value; }

    /// <summary>
    /// On exit, DOS copies this to the INT 0x24 vector.
    /// </summary>
    public uint CriticalErrorAddress { get => UInt32[14]; set => UInt32[14] = value; }

    /// <summary>
    /// Segment of PSP of parent program.
    /// </summary>
    public ushort ParentProgramSegmentPrefix { get => UInt16[18]; set => UInt16[18] = value; }

    public byte[] Files { get => GetData(20, 20); set { LoadData(20, value); } }

    public ushort EnvironmentTableSegment { get => UInt16[40]; set => UInt16[40] = value; }

    public uint StackPointer { get => UInt32[42]; set => UInt32[42] = value; }

    public ushort MaximumOpenFiles { get => UInt16[46]; set => UInt16[46] = value; }

    public uint FileTableAddress { get => UInt32[48]; set => UInt32[48] = value; }

    public uint PreviousPspAddress { get => UInt32[52]; set => UInt32[52] = value; }

    public byte InterimFlag { get => UInt8[56]; set => UInt8[56] = value; }

    public byte TrueNameFlag { get => UInt8[57]; set => UInt8[57] = value; }

    public ushort NNFlags { get => UInt16[58]; set => UInt16[58] = value; }

    public byte DosVersionMajor { get => UInt8[60]; set => UInt8[60] = value; }

    public byte DosVersionMinor { get => UInt8[61]; set => UInt8[61] = value; }

    public byte[] Unused { get => GetData(62, 14); set { LoadData(62, value); } }

    public byte[] Service { get => GetData(76, 3); set { LoadData(76, value); } }

    public byte[] Unused2 { get => GetData(79, 9); set { LoadData(79, value); } }

    public byte[] FirstFileControlBlock { get => GetData(88, 16); set { LoadData(88, value); } }

    public byte[] SecondFileControlBlock { get => GetData(104, 16); set { LoadData(104, value); } }

    public byte[] Unused3 { get => GetData(120, 4); set { LoadData(120, value); } }
}
