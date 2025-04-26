namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;

/// <summary>
/// Represents a DOS command line.
/// </summary>
public class DosCommandTail : MemoryBasedDataStructure {
    public DosCommandTail(IByteReaderWriter byteReaderWriter, uint baseAddress)
        : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// A zero-terminated string that contains the command line arguments.
    /// </summary>
    public string Command {
        get => GetZeroTerminatedString(BaseAddress, MaxCharacterLength);
        set => SetZeroTerminatedString(BaseAddress, value, MaxCharacterLength);
    }

    /// <summary>
    /// The DOS limit for the maximum length of a command line.
    /// </summary>
    public const int MaxCharacterLength = 128;
}
