namespace Spice86.Core.Emulator.Memory;

using Spice86.Core.Emulator.Errors;

using System.Text;

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

    public static string GetZeroTerminatedString(byte[] memory, uint address, int maxLength) {
        StringBuilder res = new();
        for (int i = 0; i < maxLength; i++) {
            byte characterByte = GetUint8(memory, (uint)(address + i));
            if (characterByte == 0) {
                break;
            }
            char character = Convert.ToChar(characterByte);
            res.Append(character);
        }
        return res.ToString();
    }

    public static void SetZeroTerminatedString(byte[] memory, uint address, string value, int maxLength) {
        if (value.Length + 1 > maxLength) {
            throw new UnrecoverableException($"String {value} is more than {maxLength} cannot write it at offset {address}");
        }
        int i = 0;
        for (; i < value.Length; i++) {
            char character = value[i];
            byte charFirstByte = Encoding.ASCII.GetBytes(character.ToString())[0];
            SetUint8(memory, (uint)(address + i), charFirstByte);
        }
        SetUint8(memory, (uint)(address + i), 0);
    }
    
    public static void SetZeroTerminatedString(Memory memory, uint address, string value, int maxLength) {
        if (value.Length + 1 > maxLength) {
            throw new UnrecoverableException($"String {value} is more than {maxLength} cannot write it at offset {address}");
        }
        int i = 0;
        for (; i < value.Length; i++) {
            char character = value[i];
            byte charFirstByte = Encoding.ASCII.GetBytes(character.ToString())[0];
            memory.SetUint8((uint)(address + i), charFirstByte);
        }
        memory.SetUint8((uint)(address + i), 0);
    }

    public static string GetZeroTerminatedString(Memory memory, uint address, int maxLength) {
        StringBuilder res = new();
        for (int i = 0; i < maxLength; i++) {
            byte characterByte = memory.GetUint8((uint)(address + i));
            if (characterByte == 0) {
                break;
            }
            char character = Convert.ToChar(characterByte);
            res.Append(character);
        }
        return res.ToString();
    }
}