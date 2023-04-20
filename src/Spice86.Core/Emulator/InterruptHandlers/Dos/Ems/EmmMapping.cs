namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

public class EmmMapping {
    public ushort Handle { get; set; } = ExpandedMemoryManager.EmmNullHandle;
    public ushort LogicalPage { get; set; } = ExpandedMemoryManager.EmmNullPage;
}