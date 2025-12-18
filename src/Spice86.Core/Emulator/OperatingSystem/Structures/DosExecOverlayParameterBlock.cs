namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;

/// <summary>
/// Overlay parameter block for INT 21h AH=4Bh AL=03h.
/// </summary>
public sealed class DosExecOverlayParameterBlock : MemoryBasedDataStructure {
    public DosExecOverlayParameterBlock(IByteReaderWriter byteReaderWriter, uint baseAddress) : base(byteReaderWriter, baseAddress) {
    }

    public ushort LoadSegment {
        get => UInt16[0x00];
        set => UInt16[0x00] = value;
    }

    public ushort RelocationFactor {
        get => UInt16[0x02];
        set => UInt16[0x02] = value;
    }
}
