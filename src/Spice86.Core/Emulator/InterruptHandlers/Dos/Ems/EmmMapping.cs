namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using Spice86.Core.Emulator.Memory;

public class EmmMapping : IMemoryDevice {
    
    public uint DestAddress { get; set; }

    public ushort Handle { get; set; } = ExpandedMemoryManager.EmmNullHandle;
    public ushort LogicalPage { get; set; } = ExpandedMemoryManager.EmmNullPage;
    public int PhysicalPage { get; set; } = -1;
    public uint Size { get; } = ExpandedMemoryManager.EmmPageSize;

    public Ram Ram { get; init; } = new(ExpandedMemoryManager.EmmPageSize);
    
    public byte Read(uint address) {
        return Ram.Read(address);
    }

    public void Write(uint address, byte value) {
        Ram.Write(address, value);
    }

    public Span<byte> GetSpan(int address, int length) {
        return Ram.GetSpan(address, length);
    }
}