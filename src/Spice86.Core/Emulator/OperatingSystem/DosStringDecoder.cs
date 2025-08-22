namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Utils;

using System;
using System.Linq;
using System.Text;

/// <summary>
/// The class is responsible for decoding DOS strings with the currently set DOS Encoding.
/// </summary>
public class DosStringDecoder {
    public const ushort MaxDosStringLength = 256;

    private readonly IMemory _memory;
    private readonly State _state;

    public DosStringDecoder(IMemory memory, State state) {
        _memory = memory;
        Encoding = Encoding.GetEncoding("ibm850");
        _state = state;
    }

    static DosStringDecoder() {
        // Register the legacy code pages from the nuget package System.Text.Encoding.CodePages
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Encoding used to decode DOS strings. Default is Code Page 850 (IBM PC Western DOS encoding).
    /// </summary>
    public Encoding Encoding { get; set; }

    /// <summary>
    /// Converts a single DOS character byte to a string using the current encoding.
    /// </summary>
    public string ConvertSingleDosChar(byte characterByte) {
        return ConvertDosChars([characterByte]);
    }

    /// <summary>
    /// Converts an array of DOS character bytes to a string using the current encoding.
    /// </summary>
    public string ConvertDosChars(byte[] characterBytes) {
        return ConvertDosChars(characterBytes.AsSpan());
    }
    
    /// <summary>
    /// Converts a span of DOS character bytes to a string using the current encoding.
    /// </summary>
    public string ConvertDosChars(ReadOnlySpan<byte> characterBytes) {
        return Encoding.GetString(characterBytes);
    }

    /// <summary>
    /// Gets a string from the memory at the given segment and offset, until the given end character is found.
    /// </summary>
    /// <param name="segment">The segment part of the start address.</param>
    /// <param name="offset">The offset part of the start address.</param>
    /// <param name="end">The end character. Usually zero.</param>
    /// <returns>The string from memory.</returns>
    public string GetDosString(ushort segment, ushort offset, char end) {
        uint address = MemoryUtils.ToPhysicalAddress(segment, offset);
        byte endByte = (byte)end;
        Span<byte> data = stackalloc byte[MaxDosStringLength];
        int dataIndex = 0;

        while (dataIndex < MaxDosStringLength) {
            byte memByte = _memory.UInt8[address + (uint)dataIndex];
            if (memByte == endByte) {
                break;
            }

            data[dataIndex++] = memByte;
        }

        return ConvertDosChars(data[..dataIndex]);
    }


    /// <summary>
    /// Gets a zero terminated string from the memory at DS:DX.
    /// </summary>
    /// <returns>The string from memory.</returns>
    public string GetZeroTerminatedStringAtDsDx() {
        return GetDosString(_state.DS, _state.DX, '\0');
    }

}
