namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using Spice86.Core.Emulator.Memory;

public struct EmmMapping : IMemoryDevice {
    
    public uint DestAddress { get; set; }

    public EmmMapping() {
        Handle = 0;
        Page = 0;
        Size = Ram.Size;
    }

    public ushort Handle { get; set; } = ExpandedMemoryManager.EmmNullHandle;
    public ushort Page { get; set; } = ExpandedMemoryManager.EmmNullPage;

    public Ram Ram { get; set; } = new(ExpandedMemoryManager.EmmPageSize);
    public uint Size { get; }
    
    public byte Read(uint address) {
        return Ram.Read(address - DestAddress);
    }

    public void Write(uint address, byte value) {
        Ram.Write(address - DestAddress, value);
    }

    public Span<byte> GetSpan(int address, int length) {
        return Ram.GetSpan((int)(address - DestAddress), length);
    }
}