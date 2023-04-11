namespace Spice86.Core.Emulator.ReverseEngineer;

using Spice86.Core.Emulator.Memory;

using Spice86.Core.Emulator.Errors;
using Spice86.Shared.Emulator.Errors;

using System.Text;

public abstract class MemoryBasedDataStructureWithBaseAddressProvider : MemoryBasedDataStructure {
    protected MemoryBasedDataStructureWithBaseAddressProvider(Memory memory) : base(memory) {
    }

    public abstract uint BaseAddress { get; }

    public ushort GetUint16(int offset) {
        return GetUint16(BaseAddress, offset);
    }

    public Uint16Array GetUint16Array(int start, int length) {
        return GetUint16Array(BaseAddress, start, length);
    }

    public uint GetUint32(int offset) {
        return GetUint32(BaseAddress, offset);
    }

    public byte GetUint8(int offset) {
        return GetUint8(BaseAddress, offset);
    }

    public Uint8Array GetUint8Array(int start, int length) {
        return GetUint8Array(BaseAddress, start, length);
    }

    public string GetZeroTerminatedString(int start, int maxLength) {
        StringBuilder res = new();
        uint physicalStart = (uint)(BaseAddress + start);
        for (int i = 0; i < maxLength; i++) {
            byte characterByte = GetUint8(physicalStart, i);
            if (characterByte == 0) {
                break;
            }
            char character = Convert.ToChar(characterByte);
            res.Append(character);
        }

        return res.ToString();
    }

    public void SetUint16(int offset, ushort value) {
        SetUint16(BaseAddress, offset, value);
    }

    public void SetUint32(int offset, uint value) {
        SetUint32(BaseAddress, offset, value);
    }

    public void SetUint8(int offset, byte value) {
        SetUint8(BaseAddress, offset, value);
    }

    public void SetZeroTerminatedString(int start, string value, int maxLength) {
        if (value.Length + 1 > maxLength) {
            throw new UnrecoverableException($"String {value} is more than {maxLength} cannot write it at offset {start}");
        }

        uint physicalStart = (uint)(BaseAddress + start);
        int i = 0;
        for (; i < value.Length; i++) {
            char character = value[i];
            byte charFirstByte = Encoding.ASCII.GetBytes(character.ToString())[0];
            SetUint8(physicalStart, i, charFirstByte);
        }

        SetUint8(physicalStart, i, 0);
    }
}