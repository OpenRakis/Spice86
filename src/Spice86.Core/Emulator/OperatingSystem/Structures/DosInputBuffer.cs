namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Small structure used to represent a DOS input buffer for DOS INT21H 0xA function.
/// </summary>
public class DosInputBuffer : MemoryBasedDataStructure {
    public DosInputBuffer(IByteReaderWriter byteReaderWriter, SegmentedAddress baseAddress) : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// The actual address of the input string.
    /// </summary>
    public const int ActualInputStringOffset = 0x2;

    /// <summary>
    /// Maximum number of characters that can be read from the input buffer.
    /// </summary>
    public byte Length => UInt8[0];

    /// <summary>
    /// CALL: Number of characters from last input which may be recalled. <br/>
    /// RETURN: Number of characters actually read from the input buffer, excluding Carriage Return.
    /// </summary>
    public byte ReadCount {
        get => UInt8[1];
        set => UInt8[1] = value;
    }

    /// <summary>
    /// Actual characters read from the input buffer, including Carriage Return.
    /// </summary>
    public string Characters {
        get => GetZeroTerminatedString(ActualInputStringOffset, 255);
        set => SetZeroTerminatedString(ActualInputStringOffset, value, 255);
    }
}
