namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

public struct EmmMapping {
    public EmmMapping() {
        Handle = 0;
        Page = 0;
    }

    public ushort Handle { get; set; } = ExpandedMemoryManager.EmmNullHandle;
    public ushort Page { get; set; } = ExpandedMemoryManager.EmmNullPage;

    public byte[] Data { get; set; } = new byte[ExpandedMemoryManager.EmmPageSize];
}