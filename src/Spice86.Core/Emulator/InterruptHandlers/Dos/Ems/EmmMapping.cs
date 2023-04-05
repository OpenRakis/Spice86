namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using System.Linq;

public struct EmmMapping {
    public EmmMapping() {
        Handle = 0;
        Page = 0;
    }

    public ushort Handle { get; set; }
    public ushort Page { get; set; }

    public byte[] Data { get; set; } = new byte[ExpandedMemoryManager.EmmPageSize];
}