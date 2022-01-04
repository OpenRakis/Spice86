namespace Spice86.Utils;

using Spice86.Emulator.Memory;

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

public class ConvertUtils
{
    private const string HexStringStartPattern = "0x";

    private static readonly int SegmentSize = 0x10000;

    public static string ByteArrayToHexString(byte[] value)
    {
        StringBuilder stringBuilder = new(value.Length * 2);
        for (int i = 0; i < value.Length; i++)
        {
            byte b = value[i];
            stringBuilder.Append($"{b:X2}");
        }

        return stringBuilder.ToString();
    }

    public static long BytesToInt32(byte[] data, int start)
    {
        return Uint32(((data[start] << 24) & 0xFF000000) | ((data[start + 1] << 16) & 0x00FF0000) | ((data[start + 2] << 8) & 0x0000FF00) | ((data[start + 3]) & 0x000000FF));
    }

    public static byte[] HexToByteArray(string @string)
    {
        byte[] res = new byte[@string.Length / 2];
        for (int i = 0; i < @string.Length; i += 2)
        {
            string hex = @string.Substring(i, i + 2);
            var value = byte.Parse(hex, NumberStyles.HexNumber);
            res[i / 2] = value;
        }

        return res;
    }

    /// <summary> Sign extend value considering it is a 16 bit value </summary>
    /// <param name="value"> </param>
    /// <returns> the value sign extended </returns>
    public static int Int16(int value)
    {
        return (short)value;
    }

    /// <summary> Sign extend value considering it is a 8 bit value </summary>
    /// <param name="value"> </param>
    /// <returns> the value sign extended </returns>
    public static int Int8(int value)
    {
        return (byte)value;
    }

    /// <summary> </summary>
    /// <param name="value"> </param>
    /// <returns> a long since unsigned ints are not a thing in java </returns>
    public static long ParseHex32(string value)
    {
        string hex = new Regex(HexStringStartPattern).Replace(value, "");
        return long.Parse(hex, NumberStyles.HexNumber);
    }

    public static int ReadLsb(int value)
    {
        return Uint8(value);
    }

    public static int ReadMsb(int value)
    {
        return Uint8(value >> 8);
    }

    public static int Swap32(int value)
    {
        return (int)(((value >> 24) & 0x000000ff) | ((value >> 8) & 0x0000ff00) | ((value << 8) & 0x00ff0000) | ((value << 24) & 0xff000000));
    }

    public static int ToAbsoluteOffset(int physicalAddress)
    {
        return physicalAddress - (physicalAddress / SegmentSize) * SegmentSize;
    }

    public static int ToAbsoluteSegment(int physicalAddress)
    {
        return ((physicalAddress / SegmentSize) * SegmentSize) >> 4;
    }

    public static string ToAbsoluteSegmentedAddress(int segment, int offset)
    {
        int physical = MemoryUtils.ToPhysicalAddress(segment, offset);
        return $"{ToHex16(ToAbsoluteSegment(physical))}:{ToHex16(ToAbsoluteOffset(physical))}";
    }

    public static string ToBin16(int value)
    {
        return Uint16(value).ToString(CultureInfo.InvariantCulture);
    }

    public static string ToBin8(int value)
    {
        return Uint8(value).ToString(CultureInfo.InvariantCulture);
    }

    public static char ToChar(int value)
    {
        return Encoding.ASCII.GetString(new byte[] { (byte)value }).ToCharArray()[0];
    }

    public static string ToCSharpString(SegmentedAddress address)
    {
        return $"{ToHex16WithoutX(address.GetSegment())}_{ToHex16WithoutX(address.GetOffset())}";
    }

    public static string ToCSharpStringWithPhysical(SegmentedAddress address)
    {
        return $"{ToHex16WithoutX(address.GetSegment())}_{ToHex16WithoutX(address.GetOffset())}_{ToHex16WithoutX(address.ToPhysical())}";
    }

    public static string ToHex(byte value)
    {
        return $"0x{value:x}";
    }

    public static string ToHex(short value)
    {
        return $"0x{value:x}";
    }

    public static string ToHex(int value)
    {
        return $"0x{value:x}";
    }

    public static string ToHex16(int value)
    {
        return $"0x{Uint16(value):x}";
    }

    public static string ToHex16WithoutX(int value)
    {
        return $"{Uint16(value):x}";
    }

    public static string ToHex8(int value)
    {
        return $"0x{Uint8(value):x}";
    }

    public static string ToSegmentedAddressRepresentation(int segment, int offset)
    {
        return $"{ToHex16(segment)}:{ToHex16(offset)}";
    }

    public static string ToString(byte[] value)
    {
        return Encoding.ASCII.GetString(value);
    }

    public static int Uint16(int value)
    {
        return value & 0xFFFF;
    }

    public static long Uint32(long value)
    {
        return value & 4294967295L;
    }

    public static int Uint32i(long value)
    {
        return (int)Uint32(value);
    }

    public static int Uint8(int value)
    {
        return value & 0xFF;
    }

    public static byte Uint8b(int value)
    {
        return (byte)Uint8(value);
    }

    public static int WriteLsb(int value, int lsb)
    {
        return (value & 0xFF00) | Uint8(lsb);
    }

    public static int WriteMsb(int value, int msb)
    {
        return (value & 0x00FF) | ((msb << 8) & 0xFF00);
    }
}