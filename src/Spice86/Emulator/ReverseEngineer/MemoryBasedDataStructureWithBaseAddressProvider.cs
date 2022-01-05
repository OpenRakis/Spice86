namespace Spice86.Emulator.ReverseEngineer;

using Spice86.Emulator.Errors;
using Spice86.Emulator.Memory;

using System.Text;

public abstract class MemoryBasedDataStructureWithBaseAddressProvider : MemoryBasedDataStructure
{
    protected MemoryBasedDataStructureWithBaseAddressProvider(Memory memory) : base(memory)
    {
    }

    public abstract int GetBaseAddress();

    public ushort GetUint16(int offset)
    {
        return base.GetUint16(GetBaseAddress(), offset);
    }

    public Uint16Array GetUint16Array(int start, int length)
    {
        return base.GetUint16Array(GetBaseAddress(), start, length);
    }

    public uint GetUint32(int offset)
    {
        return base.GetUint32(GetBaseAddress(), offset);
    }

    public byte GetUint8(int offset)
    {
        return base.GetUint8(GetBaseAddress(), offset);
    }

    public Uint8Array GetUint8Array(int start, int length)
    {
        return base.GetUint8Array(GetBaseAddress(), start, length);
    }

    public string GetZeroTerminatedString(int start, int maxLength)
    {
        StringBuilder res = new();
        int physicalStart = GetBaseAddress() + start;
        for (int i = 0; i < maxLength; i++)
        {
            char character = (char)base.GetUint8(physicalStart, i);
            if (character == 0)
            {
                break;
            }

            res.Append(character);
        }

        return res.ToString();
    }

    public void SetUint16(int offset, ushort value)
    {
        base.SetUint16(GetBaseAddress(), offset, value);
    }

    public void SetUint32(int offset, uint value)
    {
        base.SetUint32(GetBaseAddress(), offset, value);
    }

    public void SetUint8(int offset, byte value)
    {
        base.SetUint8(GetBaseAddress(), offset, value);
    }

    public void SetZeroTerminatedString(int start, string value, int maxLenght)
    {
        if (value.Length + 1 > maxLenght)
        {
            throw new UnrecoverableException($"String {value} is more than {maxLenght} cannot write it at offset {start}");
        }

        int physicalStart = GetBaseAddress() + start;
        int i = 0;
        for (; i < value.Length; i++)
        {
            char character = value[i];
            var charFirstByte = Encoding.ASCII.GetBytes(character.ToString())[0];
            base.SetUint8(physicalStart, i, charFirstByte);
        }

        base.SetUint8(physicalStart, i, 0);
    }
}