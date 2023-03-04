namespace Spice86.Core.Emulator.Memory;

using Spice86.Core.Emulator.VM;

/// <summary>
/// Represents the main memory of the IBM PC.
/// Size must be at least 1 MB.
/// </summary>
public class MainMemory : Memory {
    /// <summary>
    /// Size of conventional memory in bytes.
    /// </summary>
    public const uint ConvMemorySize = 1024 * 1024;

    private readonly Machine _machine;
    
    public MainMemory(Machine machine, uint sizeInKb) : base(machine, sizeInKb) {
        _machine = machine;
        if (sizeInKb * 1024 < ConvMemorySize) {
            throw new ArgumentException("Memory size must be at least 1 MB.");
        }
    }
    
    public override void SetUint32(uint address, uint value) {
        if (_machine.Ems?.TryWriteMappedPageData(address, value) is false or null) {
            base.SetUint32(address, value);
        }
    }
    
    public override void SetUint16(uint address, ushort value) {
        if (_machine.Ems?.TryWriteMappedPageData(address, value) is false or null) {
            base.SetUint16(address, value);
        }
    }

    public override void SetUint8(uint address, byte value) {
        if (_machine.Ems?.TryWriteMappedPageData(address, value) is false or null) {
            base.SetUint8(address, value);
        }
    }

    public override uint GetUint32(uint address) {
        return _machine.Ems?.TryGetMappedPageData(address, out uint data) is true ? data : base.GetUint32(address);
    }

    public override ushort GetUint16(uint address) {
        return _machine.Ems?.TryGetMappedPageData(address, out ushort data) is true ? data : base.GetUint16(address);
    }

    public override byte GetUint8(uint address) {
        return _machine.Ems?.TryGetMappedPageData(address, out byte data) is true ? data : base.GetUint8(address);
    }
}