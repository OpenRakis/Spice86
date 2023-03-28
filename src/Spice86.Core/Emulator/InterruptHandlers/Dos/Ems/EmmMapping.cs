namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using System.Linq;

public struct EmmMapping {
    public ushort Handle { get; set; }
    public ushort Page { get; set; }

    public ushort Data {
        get {
            byte[] handleBytes = BitConverter.GetBytes(Handle);
            byte[] pageBytes = BitConverter.GetBytes(Page);
            Span<byte> data = handleBytes.Union(pageBytes).ToArray().AsSpan();
            return BinaryPrimitives.ReadUInt16LittleEndian(data);
        }
    }
}