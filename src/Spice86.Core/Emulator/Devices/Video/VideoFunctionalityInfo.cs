namespace Spice86.Core.Emulator.Devices.Video;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.ReverseEngineer;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Represents a Dynamic Functionality State Table in memory. More info here: https://stanislavs.org/helppc/int_10-1b.html
/// </summary>
public class VideoFunctionalityInfo : MemoryBasedDataStructureWithBaseAddress {
    public uint SftAddress { get => GetUint32(0x00); set => SetUint32(0x00, value); }
    public byte VideoMode { get => GetUint8(0x04); set => SetUint8(0x04, value); } 
    public ushort ScreenColumns { get => GetUint16(0x05); set => SetUint16(0x05, value); }
    public ushort VideoBufferLength { get => GetUint16(0x07); set => SetUint16(0x07, value); }
    public ushort VideoBufferAddress { get => GetUint16(0x09); set => SetUint16(0x09, value); }
    public (byte, byte) GetCursorPosition([Range(0, 7)] int page) => (GetUint8(0x0B + page * 2), GetUint8(0x0C + page * 2));
    public void SetCursorPosition([Range(0, 7)] int page, byte x, byte y) {
        int offset = page * 2;
        SetUint8(0x0B + offset, x);
        SetUint8(0x0C + offset, y);
    }
    public byte CursorEndLine { get => GetUint8(0x1B); set => SetUint8(0x1B, value); }
    public byte CursorStartLine { get => GetUint8(0x1C); set => SetUint8(0x1C, value); }
    public byte ActiveDisplayPage { get => GetUint8(0x1D); set => SetUint8(0x1D, value); }
    public ushort CrtControllerBaseAddress { get => GetUint16(0x1E); set => SetUint16(0x1E, value); }
    public byte CurrentRegister3X8Value { get => GetUint8(0x20); set => SetUint8(0x20, value); }
    public byte CurrentRegister3X9Value { get => GetUint8(0x21); set => SetUint8(0x21, value); }
    public byte ScreenRows { get => GetUint8(0x22); set => SetUint8(0x22, value); }
    public ushort CharacterMatrixHeight { get => GetUint16(0x23); set => SetUint16(0x23, value); }
    public byte ActiveDisplayCombinationCode { get => GetUint8(0x25); set => SetUint8(0x25, value); }
    public byte AlternateDisplayCombinationCode { get => GetUint8(0x26); set => SetUint8(0x26, value); }
    public ushort NumberOfColorsSupported { get => GetUint16(0x27); set => SetUint16(0x27, value); }
    public byte NumberOfPages { get => GetUint8(0x29); set => SetUint8(0x29, value); }
    public byte NumberOfActiveScanLines { get => GetUint8(0x2A); set => SetUint8(0x2A, value); }
    public byte TextCharacterTableUsed { get => GetUint8(0x2B); set => SetUint8(0x2B, value); }
    public byte TextCharacterTableUsed2 { get => GetUint8(0x2C); set => SetUint8(0x2C, value); }
    public byte OtherStateInformation { get => GetUint8(0x2D); set => SetUint8(0x2D, value); }
    public byte VideoRamAvailable { get => GetUint8(0x31); set => SetUint8(0x31, value); }
    public byte SaveAreaStatus { get => GetUint8(0x32); set => SetUint8(0x32, value); }

    public VideoFunctionalityInfo(Memory memory, uint baseAddress) : base(memory, baseAddress) {
    }
}