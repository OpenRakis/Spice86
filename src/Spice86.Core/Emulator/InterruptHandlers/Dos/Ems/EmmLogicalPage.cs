namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems; 

public class EmmLogicalPage {
    public ushort Id { get; set; }
    
    public byte PhysicalPageId { get; set; }

    public EmmLogicalPage(ushort id, byte physicalPageId) {
        Id = id;
        PhysicalPageId = physicalPageId;
    }
}