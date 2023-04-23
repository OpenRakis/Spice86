namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using Spice86.Core.Emulator.Memory;

/// <summary>
/// Backing store of a logical or physical EMM Page.
/// </summary>
public class EmmPageMemory : IMemoryDevice {
    
    public uint Size => ExpandedMemoryManager.EmmPageSize;

    private Ram _ram;

    private uint _offset;

    public EmmPageMemory(uint offset) {
        _offset = offset;
        _ram = new Ram(Size);
    }
    
    public byte Read(uint address) {
        return _ram.Read(address - _offset);
    }

    public void Write(uint address, byte value) {
        _ram.Write(address - _offset, value);
    }

    public Span<byte> GetSpan(int address, int length) {
        return _ram.GetSpan((int) (address - _offset), length);
    }
}