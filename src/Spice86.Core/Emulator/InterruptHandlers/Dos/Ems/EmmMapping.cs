namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems; 

public struct EmmMapping {
    public ushort Handle { get; set; }
    public ushort Page { get; set; }
}