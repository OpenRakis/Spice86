namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Represents a DOS Execute Parameter Block (ExecParamRec) used for loading and executing programs.
/// This structure is used in INT 21h function 0x4B.
/// </summary>
public abstract class DosExecuteParameterBlock : MemoryBasedDataStructure {
    /// <summary>
    /// Initializes a new instance of the <see cref="DosExecuteParameterBlock"/> class.
    /// </summary>
    /// <param name="byteReaderWriter">Where data is read and written.</param>
    /// <param name="baseAddress">The base address of the data structure.</param>
    protected DosExecuteParameterBlock(IByteReaderWriter byteReaderWriter,
        uint baseAddress) : base(byteReaderWriter, baseAddress) {
    }
}

/// <summary>
/// Parameter block for loading and executing a program (AL=0) or just loading (AL=1).
/// </summary>
public class DosLoadOrLoadAndExecuteParameterBlock : DosExecuteParameterBlock {
    public DosLoadOrLoadAndExecuteParameterBlock(IByteReaderWriter byteReaderWriter,
        uint baseAddress) : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// Gets or sets the segment of environment for child process.
    /// 0000h means use a copy of the current environment.
    /// </summary>
    public ushort EnvironmentSegment {
        get => UInt16[0x0];
        set => UInt16[0x0] = value;
    }

    /// <summary>
    /// Gets or sets the address of command line text to place at PSP:0080.
    /// </summary>
    public uint CommandTailAddress {
        get => UInt32[0x2];
        set => UInt32[0x2] = value;
    }

    /// <summary>
    /// Gets or sets the address of the first FCB to be placed at PSP:005C.
    /// </summary>
    public uint FirstFcbAddress {
        get => UInt32[0x6];
        set => UInt32[0x6] = value;
    }

    /// <summary>
    /// Gets or sets the address of the second FCB to be placed at PSP:006C.
    /// </summary>
    public uint SecondFcbAddress {
        get => UInt32[0xA];
        set => UInt32[0xA] = value;
    }
}

/// <summary>
/// Parameter block for loading a program but not executing it (AL=1).
/// </summary>
public sealed class DosLoadProgramParameterBlock : DosLoadOrLoadAndExecuteParameterBlock {
    public DosLoadProgramParameterBlock(IByteReaderWriter byteReaderWriter,
        uint baseAddress) : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// Gets or sets the initial CS:IP of the loaded program. This is a return value.
    /// </summary>
    public SegmentedAddress EntryPointAddress {
        get => SegmentedAddress32[0xE];
        set => SegmentedAddress32[0xE] = value;
    }

    /// <summary>
    /// Gets or sets the initial SS:SP of the loaded program. This is a return value.
    /// </summary>
    public SegmentedAddress StackAddress {
        get => SegmentedAddress32[0x12];
        set => SegmentedAddress32[0x12] = value;
    }
}

/// <summary>
/// Parameter block for loading an overlay (AL=3).
/// </summary>
public sealed class DosOverlayParameterBlock : DosExecuteParameterBlock {
    public DosOverlayParameterBlock(IByteReaderWriter byteReaderWriter,
        uint baseAddress) : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// Gets or sets the segment address where the file will be loaded.
    /// </summary>
    public ushort OverlayLoadSegment {
        get => UInt16[0x0];
        set => UInt16[0x0] = value;
    }

    /// <summary>
    /// Gets or sets the relocation factor to be applied to the image.
    /// </summary>
    public ushort OverlayRelocationFactor {
        get => UInt16[0x2];
        set => UInt16[0x2] = value;
    }
}
