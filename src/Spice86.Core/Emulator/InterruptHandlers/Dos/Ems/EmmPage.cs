namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using Spice86.Core.Emulator.Memory;

public class EmmPage : IEmmPage {
    public EmmPage() {
        PageMemory = new EmmPageMemory();
    }
    
    public IMemoryDevice PageMemory { get; set; }

    public ushort PageNumber { get; set; } = ExpandedMemoryManager.EmmNullPage;
}