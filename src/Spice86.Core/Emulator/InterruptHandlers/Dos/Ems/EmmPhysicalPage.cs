namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using Spice86.Core.Emulator.Memory;

public class EmmPhysicalPage : IMemoryDevice {
    public byte Number { get; init; }
    
    public uint Size => ExpandedMemoryManager.EmmPageSize;

    private Ram _ram;

    public EmmPhysicalPage(byte physicalPageNumber) {
        Number = physicalPageNumber;
        _ram = new Ram(Size);
    }
    
    public byte Read(uint address) {
        return _ram.Read(address);
    }

    public void Write(uint address, byte value) {
        _ram.Write(address, value);
    }

    public Span<byte> GetSpan(int address, int length) {
        return _ram.GetSpan(address, length);
    }
}