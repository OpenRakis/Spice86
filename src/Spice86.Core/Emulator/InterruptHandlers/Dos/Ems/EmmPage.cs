namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using Spice86.Core.Emulator.Memory;

public class EmmPage {
    public EmmPage(uint size) {
        PageMemory = new Ram(size);
    }
    
    public Ram PageMemory { get; set; }

    public ushort PageNumber { get; set; } = ExpandedMemoryManager.EmmNullPage;
}