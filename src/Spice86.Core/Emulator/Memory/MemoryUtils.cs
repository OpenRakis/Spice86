namespace Spice86.Core.Emulator.Memory;

/// <summary>
/// Utils to get and set values in an array. Words and DWords are considered to be stored
/// little-endian. <br />
/// </summary>
public static class MemoryUtils {
    public static ushort GetUint16(byte[] memory, uint address) {
        return (ushort)(memory[address] | memory[address + 1] << 8);
    }

    public static uint GetUint32(byte[] memory, uint address) {
        return (uint)(memory[address] | memory[address + 1] << 8 | memory[address + 2] << 16 | memory[address + 3] << 24);
    }

    public static byte GetUint8(byte[] memory, uint address) {
        return memory[address];
    }

    public static void SetUint16(byte[] memory, uint address, ushort value) {
        memory[address] = (byte)value;
        memory[address + 1] = (byte)(value >> 8);
    }

    public static void SetUint32(byte[] memory, uint address, uint value) {
        memory[address] = (byte)value;
        memory[address + 1] = (byte)(value >> 8);
        memory[address + 2] = (byte)(value >> 16);
        memory[address + 3] = (byte)(value >> 24);
    }

    public static void SetUint8(byte[] memory, uint address, byte value) {
        memory[address] = value;
    }

    public static uint ToPhysicalAddress(ushort segment, ushort offset) {
        return (uint)(segment << 4) + offset;
    }

    public static ushort ToSegment(uint physicalAddress) {
        return (ushort)(physicalAddress >> 4);
    }
}