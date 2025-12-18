namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;

/// <summary>
/// Parameter block for INT 21h AH=4Bh load/execute.
/// Based on MS-DOS 4.0 EXEC.ASM.
/// </summary>
public sealed class DosExecParameterBlock : MemoryBasedDataStructure {
    public DosExecParameterBlock(IByteReaderWriter byteReaderWriter, uint baseAddress) : base(byteReaderWriter, baseAddress) {
    }

    public ushort EnvironmentSegment {
        get => UInt16[0x00];
        set => UInt16[0x00] = value;
    }

    public ushort CommandTailOffset {
        get => UInt16[0x02];
        set => UInt16[0x02] = value;
    }

    public ushort CommandTailSegment {
        get => UInt16[0x04];
        set => UInt16[0x04] = value;
    }

    public ushort FirstFcbOffset {
        get => UInt16[0x06];
        set => UInt16[0x06] = value;
    }

    public ushort FirstFcbSegment {
        get => UInt16[0x08];
        set => UInt16[0x08] = value;
    }

    public ushort SecondFcbOffset {
        get => UInt16[0x0A];
        set => UInt16[0x0A] = value;
    }

    public ushort SecondFcbSegment {
        get => UInt16[0x0C];
        set => UInt16[0x0C] = value;
    }

    public ushort InitialSS {
        get => UInt16[0x0E];
        set => UInt16[0x0E] = value;
    }

    public ushort InitialSP {
        get => UInt16[0x10];
        set => UInt16[0x10] = value;
    }

    public ushort InitialCS {
        get => UInt16[0x12];
        set => UInt16[0x12] = value;
    }

    public ushort InitialIP {
        get => UInt16[0x14];
        set => UInt16[0x14] = value;
    }
}
