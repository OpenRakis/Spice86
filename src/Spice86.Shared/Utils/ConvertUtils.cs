namespace Spice86.Shared.Utils;

using Spice86.Shared.Emulator.Memory;

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Provides utilities for converting between different data types and formats.
/// </summary>
public static class ConvertUtils {
    private const string HexStringStartPattern = "0x";

    private const uint SegmentSize = 0x10000;

    /// <summary>
    /// Converts a byte array to a hexadecimal string.
    /// </summary>
    /// <param name="value">The byte array to convert.</param>
    /// <returns>The hexadecimal representation of the byte array.</returns>
    public static string ByteArrayToHexString(byte[] value) {
        StringBuilder stringBuilder = new(value.Length * 2);
        foreach (byte b in value) {
            stringBuilder.AppendFormat("{0:X2}", b);
        }

        return stringBuilder.ToString();
    }

    /// <summary>
    /// Converts a byte array to a 32-bit integer.
    /// </summary>
    /// <param name="data">The byte array to convert.</param>
    /// <param name="start">The index of the first byte to use in the conversion.</param>
    /// <returns>The converted 32-bit integer.</returns>
    public static uint BytesToInt32(byte[] data, int start) {
        return Uint32((data[start] << 24 & 0xFF000000) | ((uint)data[start + 1] << 16 & 0x00FF0000) | ((uint)data[start + 2] << 8 & 0x0000FF00) | ((uint)data[start + 3] & 0x000000FF));
    }
    
    /// <summary>
    /// Converts a hexadecimal string to a byte array.
    /// </summary>
    /// <param name="valueString">The hexadecimal string to convert.</param>
    /// <returns>The byte array representation of the hexadecimal string.</returns>
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
    
    /// <summary>
    /// Removes any hexadecimal value starting with "0x" from the input string and returns the modified string.
    /// </summary>
    /// <param name="value">The input string to modify.</param>
    /// <returns>The modified string with any hexadecimal values removed.</returns>
    private static string Replace0xWithBlank(string value) {
        return new Regex(HexStringStartPattern).Replace(value, "");
    }
    
    /// <summary>
    /// Returns the least significant byte of a 16-bit unsigned integer.
    /// </summary>
    /// <param name="value">The input 16-bit unsigned integer.</param>
    /// <returns>The least significant byte of the input value.</returns>
    public static byte ReadLsb(ushort value) {
        return (byte)value;
    }

    /// <summary>
    /// Returns the most significant byte of a 16-bit unsigned integer.
    /// </summary>
    /// <param name="value">The input 16-bit unsigned integer.</param>
    /// <returns>The most significant byte of the input value.</returns>
    public static byte ReadMsb(ushort value) {
        return (byte)(value >> 8);
    }

    /// <summary>
    /// Swaps the byte order of a 32-bit unsigned integer and returns the result.
    /// </summary>
    /// <param name="value">The input 32-bit unsigned integer.</param>
    /// <returns>The input value with the byte order swapped.</returns>
    public static uint Swap32(uint value) {
        return (value >> 24 & 0x000000ff) | (value >> 8 & 0x0000ff00) | (value << 8 & 0x00ff0000) | (value << 24 & 0xff000000);
    }

    /// <summary>
    /// Calculates the absolute offset of a physical address in a memory segment and returns the result as a 16-bit unsigned integer.
    /// </summary>
    /// <param name="physicalAddress">The input physical address.</param>
    /// <returns>The absolute offset of the input physical address as a 16-bit unsigned integer.</returns>
    public static ushort ToAbsoluteOffset(uint physicalAddress) {
        return (ushort)(physicalAddress - (physicalAddress / SegmentSize * SegmentSize));
    }

    /// <summary>
    /// Calculates the absolute segment of a physical address in a memory segment and returns the result as a 16-bit unsigned integer.
    /// </summary>
    /// <param name="physicalAddress">The input physical address.</param>
    /// <returns>The absolute segment of the input physical address as a 16-bit unsigned integer.</returns>
    public static ushort ToAbsoluteSegment(uint physicalAddress) {
        return (ushort)(physicalAddress / SegmentSize * SegmentSize >> 4);
    }

    /// <summary>
    /// Converts a segmented address (consisting of a segment and an offset) to a segmented address string representation.
    /// </summary>
    /// <param name="segment">The input segment of the segmented address.</param>
    /// <param name="offset">The input offset of the segmented address.</param>
    /// <returns>A string representation of the segmented address in the format "0x0000:0x0000".</returns>
    public static string ToAbsoluteSegmentedAddress(ushort segment, ushort offset) {
        uint physical = MemoryUtils.ToPhysicalAddress(segment, offset);
        return $"{ToHex16(ToAbsoluteSegment(physical))}:{ToHex16(ToAbsoluteOffset(physical))}";
    }

    /// <summary>
    /// Converts a 16-bit unsigned integer to a binary string representation.
    /// </summary>
    /// <param name="value">The input 16-bit unsigned integer.</param>
    /// <returns>A binary string representation of the input value.</returns>
    public static string ToBin16(ushort value) {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Converts an 8-bit unsigned integer to a binary string representation.
    /// </summary>
    /// <param name="value">The input 8-bit unsigned integer.</param>
    /// <returns>A binary string representation of the input value.</returns>
    public static string ToBin8(byte value) {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Converts an 8-bit unsigned integer to an ASCII character.
    /// </summary>
    /// <param name="value">The input 8-bit unsigned integer.</param>
    /// <returns>An ASCII character represented by the input value.</returns>
    public static char ToChar(byte value) {
        return Encoding.ASCII.GetString(new[] { value })[0];
    }

    /// <summary>
    /// Converts a SegmentedAddress object to a C# formatted string.
    /// </summary>
    /// <param name="address">The SegmentedAddress object to convert.</param>
    /// <returns>A C# formatted string representing the SegmentedAddress object.</returns>
    public static string ToCSharpString(SegmentedAddress address) {
        return $"{ToHex16WithoutX(address.Segment)}_{ToHex16WithoutX(address.Offset)}";
    }

    /// <summary>
    /// Converts a SegmentedAddress object to a C# formatted string with physical address included.
    /// </summary>
    /// <param name="address">The SegmentedAddress object to convert.</param>
    /// <returns>A C# formatted string representing the SegmentedAddress object with physical address.</returns>
    public static string ToCSharpStringWithPhysical(SegmentedAddress address) {
        return $"{ToHex16WithoutX(address.Segment)}_{ToHex16WithoutX(address.Offset)}_{ToHex32WithoutX(address.ToPhysical())}";
    }

    /// <summary>
    /// Returns a hexadecimal string representation of the given byte value.
    /// </summary>
    /// <param name="value">The byte value to represent in hexadecimal form.</param>
    /// <returns>A string representing the byte value in hexadecimal form.</returns>
    public static string ToHex(byte value) {
        return $"0x{value:X}";
    }
    
    /// <summary>
    /// Returns a hexadecimal string representation of the given short value.
    /// </summary>
    /// <param name="value">The short value to represent in hexadecimal form.</param>
    /// <returns>A string representing the short value in hexadecimal form.</returns>
    public static string ToHex(short value) {
        return $"0x{value:X}";
    }

    /// <summary>
    /// Returns a hexadecimal string representation of the given uint value.
    /// </summary>
    /// <param name="value">The uint value to represent in hexadecimal form.</param>
    /// <returns>A string representing the uint value in hexadecimal form.</returns>
    public static string ToHex(uint value) {
        return $"0x{value:X}";
    }

    /// <summary>
    /// Returns a hexadecimal string representation of the given uint value.
    /// </summary>
    /// <param name="value">The uint value to represent in hexadecimal form.</param>
    /// <returns>A string representing the uint value in hexadecimal form.</returns>
    public static string ToHex32(uint value) {
        return $"0x{value:X}";
    }

    /// <summary>
    /// Returns a hexadecimal string representation of the given ushort value.
    /// </summary>
    /// <param name="value">The ushort value to represent in hexadecimal form.</param>
    /// <returns>A string representing the ushort value in hexadecimal form.</returns>
    public static string ToHex16(ushort value) {
        return $"0x{value:X}";
    }

    /// <summary>
    /// Returns a string representation of the given ushort value in hexadecimal form without the "0x" prefix.
    /// </summary>
    /// <param name="value">The ushort value to represent in hexadecimal form.</param>
    /// <returns>A string representing the ushort value in hexadecimal form without the "0x" prefix.</returns>
    public static string ToHex16WithoutX(ushort value) {
        return $"{value:X4}";
    }
    
    /// <summary>
    /// Returns a string representation of the given uint value in hexadecimal form without the "0x" prefix.
    /// </summary>
    /// <param name="value">The uint value to represent in hexadecimal form.</param>
    /// <returns>A string representing the uint value in hexadecimal form without the "0x" prefix.</returns>
    public static string ToHex32WithoutX(uint value) {
        return $"{value:X5}";
    }

    /// <summary>
    /// Returns a hexadecimal string representation of the given byte value.
    /// </summary>
    /// <param name="value">The byte value to represent in hexadecimal form.</param>
    /// <returns>A string representing the byte value in hexadecimal form.</returns>
    public static string ToHex8(byte value) {
        return $"0x{value:X}";
    }

    /// <summary>
    /// Returns a segmented address representation of the given segment and offset values.
    /// </summary>
    /// <param name="segment">The segment value.</param>
    /// <param name="offset">The offset value.</param>
    /// <returns>A string representing the segmented address in the format "segment:offset".</returns>
    public static string ToSegmentedAddressRepresentation(ushort segment, ushort offset) {
        return $"{ToHex16(segment)}:{ToHex16(offset)}";
    }

    /// <summary>
    /// Returns a string representation of the given byte array in ASCII format.
    /// </summary>
    /// <param name="value">The byte array to convert to a string.</param>
    /// <returns>A string representation of the byte array in ASCII format.</returns>
    public static string ToString(byte[] value) {
        return Encoding.ASCII.GetString(value);
    }

    /// <summary>
    /// Returns a string representation of the given byte span in ASCII format.
    /// </summary>
    /// <param name="value">The byte span to convert to a string.</param>
    /// <returns>A string representation of the byte span in ASCII format.</returns>
    public static string ToString(Span<byte> value) {
        return Encoding.ASCII.GetString(value);
    }
    
    /// <summary>
    /// Returns the lower 16 bits of the given ushort value.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The lower 16 bits of the given ushort value.</returns>
    public static ushort Uint16(ushort value) {
        return (ushort)(value & 0xFFFF);
    }

    /// <summary>
    /// Returns the lower 32 bits of the given long value.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The lower 32 bits of the given long value.</returns>
    public static uint Uint32(long value) {
        return (uint)(value & 4294967295L);
    }

    /// <summary>
    /// Returns the lower 32 bits of the given long value as an int.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The lower 32 bits of the given long value as an int.</returns>
    public static int Uint32i(long value) {
        return (int)Uint32(value);
    }

    /// <summary>
    /// Returns the lower 8 bits of the given byte value.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The lower 8 bits of the given byte value.</returns>
    public static byte Uint8(byte value) {
        return (byte)(value & 0xFF);
    }

    /// <summary>
    /// Returns the lower 8 bits of the given byte value as a signed byte.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The lower 8 bits of the given byte value as a signed byte.</returns>
    public static sbyte Uint8b(byte value) {
        return (sbyte)Uint8(value);
    }

    /// <summary>
    /// Returns a new ushort value with the lower 8 bits replaced with the given byte value.
    /// </summary>
    /// <param name="value">The original ushort value.</param>
    /// <param name="lsb">The value to replace the lower 8 bits with.</param>
    /// <returns>A new ushort value with the lower 8 bits replaced with the given byte value.</returns>
    public static ushort WriteLsb(ushort value, byte lsb) {
        return (ushort)((value & 0xFF00) | lsb);
    }

    /// <summary>
    /// Returns a new ushort value with the higher 8 bits replaced with the given byte value.
    /// </summary>
    /// <param name="value">The original ushort value.</param>
    /// <param name="msb">The value to replace the higher 8 bits with.</param>
    /// <returns>A new ushort value with the higher 8 bits replaced with the given byte value.</returns>
    public static ushort WriteMsb(ushort value, byte msb) {
        return (ushort)((value & 0x00FF) | (msb << 8 & 0xFF00));
    }

    /// <summary>
    /// Replaces all occurrences of backslashes with forward slashes in the given path string.
    /// </summary>
    /// <param name="path">The path string to modify.</param>
    /// <returns>A new string with all backslashes replaced with forward slashes.</returns>
    public static string ToSlashPath(string path) {
        return path.Replace('\\', '/').Replace("//", "/");
    }

    /// <summary>
    /// Replaces all occurrences of backslashes with forward slashes in the given folder path string
    /// and ensures that the resulting string ends with a forward slash.
    /// </summary>
    /// <param name="path">The folder path string to modify.</param>
    public static string ToSlashFolderPath(string path) {
        string res = ToSlashPath(path);
        if (!res.EndsWith('/')) {
            res += '/';
        }
        return res;
    }
}
