namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using Spice86.Core.Emulator.Memory;

internal static class RamExtensions {
    public static void BlockCopy(this Ram memory, int destAddress, int srcAddress, int size) {
        Span<byte> src = memory.GetSpan(srcAddress, size);
        Span<byte> dest = memory.GetSpan(destAddress, size);
        src.CopyTo(dest);
    }
}
