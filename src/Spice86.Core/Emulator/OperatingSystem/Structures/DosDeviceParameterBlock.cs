namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;
using Spice86.Shared.Emulator.Memory;

public class DosDeviceParameterBlock : MemoryBasedDataStructure {
    public DosDeviceParameterBlock(IByteReaderWriter byteReaderWriter, SegmentedAddress baseAddress) : base(byteReaderWriter, baseAddress) {
    }
    
    public byte DeviceType {
        get => UInt8[0x1];
        set => UInt8[0x1] = value;
    }
    
    public ushort DeviceAttributes {
        get => UInt16[0x2];
        set => UInt16[0x2] = value;
    }
    
    public ushort Cylinders {
        get => UInt16[0x4];
        set => UInt16[0x4] = value;
    }
    
    public byte MediaType {
        get => UInt8[0x6];
        set => UInt8[0x6] = value;
    }
    
    public TruncatedBiosParameterBlock BiosParameterBlock {
        get => new (ByteReaderWriter, BaseAddress.PlusOffset(0x7));
    }
}