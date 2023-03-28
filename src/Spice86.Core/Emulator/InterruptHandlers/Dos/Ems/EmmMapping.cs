namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using System.Linq;

public struct EmmMapping {
    public ushort Handle { get; set; }
    public ushort Page { get; set; }
}