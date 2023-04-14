namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

public class EmmMapping {
    
    public uint DestAddress { get; set; }

    public EmmMapping() {
        Handle = 0;
        LogicalPage = 0;
    }

    public ushort Handle { get; set; } = ExpandedMemoryManager.EmmNullHandle;
    public ushort LogicalPage { get; set; } = ExpandedMemoryManager.EmmNullPage;
    public int PhysicalPage { get; set; }
}