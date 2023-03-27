namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using Spice86.Core.Emulator.Memory;

internal static class RamExtensions {
    public static void BlockCopy(this Ram memory, int destAddress, int srcAddress, int size) {
        Span<byte> src = memory.GetSpan(srcAddress, size);
        Span<byte> dest = memory.GetSpan(destAddress, size);
        src.CopyTo(dest);
    }

    public static void WriteWord(this Ram memory, uint address, ushort value) {
        Span<byte> src = BitConverter.GetBytes(value).AsSpan();
        Span<byte> dest = memory.GetSpan((int) address, src.Length);
        src.CopyTo(dest);
    }
}
