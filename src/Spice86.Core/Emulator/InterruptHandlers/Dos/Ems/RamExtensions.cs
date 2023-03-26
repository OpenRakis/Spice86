namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using Spice86.Core.Emulator.Memory;

using System.Text;

internal static class RamExtensions {
    public static void BlockWrite(this Ram memory, int address, string name) {
        byte[] bytes = Encoding.Unicode.GetBytes(name);
        for (int i = 0; i < bytes.Length; i++) {
            byte item = bytes[i];
            memory.Write((ushort)(address + i), item);
        }
    }
}
