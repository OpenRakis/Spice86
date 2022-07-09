namespace Spice86.Utils;

using Spice86.Emulator.Memory;

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

public static class ConvertUtils {
    private const string HexStringStartPattern = "0x";

    private const uint SegmentSize = 0x10000;

    public static string ByteArrayToHexString(byte[] value) {
        StringBuilder stringBuilder = new(value.Length * 2);
        for (int i = 0; i < value.Length; i++) {
            byte b = value[i];
            stringBuilder.AppendFormat("{0:X2}", b);
        }

        return stringBuilder.ToString();
    }

    public static uint BytesToInt32(byte[] data, int start) {
        return Uint32(((data[start] << 24) & 0xFF000000) | (((uint)data[start + 1] << 16) & 0x00FF0000) | (((uint)data[start + 2] << 8) & 0x0000FF00) | (((uint)data[start + 3]) & 0x000000FF));
    }

    public static byte[] HexToByteArray(string valueString) {
        byte[] res = new byte[valueString.Length / 2];
        for (int i = 0; i < valueString.Length; i += 2) {
            string hex = valueString.Substring(i, 2);
            res[i / 2] = byte.Parse(hex, NumberStyles.HexNumber);
        }

        return res;
    }

    /// <summary> Sign extend value considering it is a 16 bit value </summary>
    /// <param name="value"> </param>
    /// <returns> the value sign extended </returns>
    public static short Int16(ushort value) {
        return (short)value;
    }

    /// <summary> Sign extend value considering it is a 8 bit value </summary>
    /// <param name="value"> </param>
    /// <returns> the value sign extended </returns>
    public static sbyte Int8(byte value) {
        return (sbyte)value;
    }

    /// <summary>Parses a hex string as uint </summary>
    /// <param name="value"> </param>
    /// <returns>the value as a uint</returns>
    public static uint ParseHex32(string value) {
        return uint.Parse(Replace0xWithBlank(value), NumberStyles.HexNumber);
    }

    /// <summary>Parses a hex string as ushort </summary>
    /// <param name="value"> </param>
    /// <returns>the value as a ushort</returns>
    public static ushort ParseHex16(string value) {
        return ushort.Parse(Replace0xWithBlank(value), NumberStyles.HexNumber);
    }
    private static string Replace0xWithBlank(string value) {
        return new Regex(HexStringStartPattern).Replace(value, "");
    }
    public static byte ReadLsb(ushort value) {
        return (byte)value;
    }

    public static byte ReadMsb(ushort value) {
        return (byte)(value >> 8);
    }

    public static uint Swap32(uint value) {
        return ((value >> 24) & 0x000000ff) | ((value >> 8) & 0x0000ff00) | ((value << 8) & 0x00ff0000) | ((value << 24) & 0xff000000);
    }

    public static ushort ToAbsoluteOffset(uint physicalAddress) {
        return (ushort)(physicalAddress - (physicalAddress / SegmentSize * SegmentSize));
    }

    public static ushort ToAbsoluteSegment(uint physicalAddress) {
        return (ushort)((physicalAddress / SegmentSize * SegmentSize) >> 4);
    }

    public static string ToAbsoluteSegmentedAddress(ushort segment, ushort offset) {
        uint physical = MemoryUtils.ToPhysicalAddress(segment, offset);
        return $"{ToHex16(ToAbsoluteSegment(physical))}:{ToHex16(ToAbsoluteOffset(physical))}";
    }

    public static string ToBin16(ushort value) {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    public static string ToBin8(byte value) {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    public static char ToChar(byte value) {
        return Encoding.ASCII.GetString(new[] { value })[0];
    }

    public static string ToCSharpString(SegmentedAddress address) {
        return $"{ToHex16WithoutX(address.Segment)}_{ToHex16WithoutX(address.Offset)}";
    }

    public static string ToCSharpStringWithPhysical(SegmentedAddress address) {
        return $"{ToHex16WithoutX(address.Segment)}_{ToHex16WithoutX(address.Offset)}_{ToHex32WithoutX(address.ToPhysical())}";
    }

    public static string ToHex(byte value) {
        return $"0x{value:X}";
    }

    public static string ToHex(short value) {
        return $"0x{value:X}";
    }

    public static string ToHex(uint value) {
        return $"0x{value:X}";
    }

    public static string ToHex16(ushort value) {
        return $"0x{value:X}";
    }

    public static string ToHex16WithoutX(ushort value) {
        return $"{value:X4}";
    }
    public static string ToHex32WithoutX(uint value) {
        return $"{value:X5}";
    }

    public static string ToHex8(byte value) {
        return $"0x{value:X}";
    }

    public static string ToSegmentedAddressRepresentation(ushort segment, ushort offset) {
        return $"{ToHex16(segment)}:{ToHex16(offset)}";
    }

    public static string ToString(byte[] value) {
        return Encoding.ASCII.GetString(value);
    }

    public static string ToString(Span<byte> value) {
        return Encoding.ASCII.GetString(value);
    }
    
    public static ushort Uint16(ushort value) {
        return (ushort)(value & 0xFFFF);
    }

    public static uint Uint32(long value) {
        return (uint)(value & 4294967295L);
    }

    public static int Uint32i(long value) {
        return (int)Uint32(value);
    }

    public static byte Uint8(byte value) {
        return (byte)(value & 0xFF);
    }

    public static sbyte Uint8b(byte value) {
        return (sbyte)Uint8(value);
    }

    public static ushort WriteLsb(ushort value, byte lsb) {
        return (ushort)((value & 0xFF00) | lsb);
    }

    public static ushort WriteMsb(ushort value, byte msb) {
        return (ushort)((value & 0x00FF) | ((msb << 8) & 0xFF00));
    }

    public static string ToSlashPath(string path) {
        return path.Replace('\\', '/').Replace("//", "/");
    }

    public static string ToSlashFolderPath(string path) {
        string res = ToSlashPath(path);
        if (!res.EndsWith('/')) {
            res += '/';
        }
        return res;
    }
}