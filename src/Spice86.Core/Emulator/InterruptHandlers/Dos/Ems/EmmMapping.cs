namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using System.Linq;

public struct EmmMapping {
    public ushort Handle { get; set; }
    public ushort Page { get; set; }

    
    public ushort Data {
        get {
            Span<byte> handleBytes = BitConverter.GetBytes(Handle);
            Span<byte> pageBytes = BitConverter.GetBytes(Page);
            Span<byte> data = stackalloc byte[] {handleBytes[0], handleBytes[1], pageBytes[0], pageBytes[1]};
            return BinaryPrimitives.ReadUInt16LittleEndian(data);
        }
    }
}