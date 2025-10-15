namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Shared.Emulator.Memory;

public class BiosParameterBlock : TruncatedBiosParameterBlock {
    public BiosParameterBlock(IByteReaderWriter byteReaderWriter, SegmentedAddress baseAddress) : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// This field is often unused or part of an extended BPB, but included for completeness.
    /// </summary>
    public byte DeviceType {
        get => UInt8[0x21];
        set => UInt8[0x21] = value;
    }

    /// <summary>
    /// This field is often unused or part of an extended BPB, but included for completeness.
    /// </summary>
    public ushort DeviceAttributes {
        get => UInt16[0x22];
        set => UInt16[0x22] = value;
    }
}