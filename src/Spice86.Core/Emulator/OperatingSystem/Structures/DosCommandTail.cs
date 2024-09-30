namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;

public class DosCommandTail : MemoryBasedDataStructure {
    public DosCommandTail(IByteReaderWriter byteReaderWriter, uint baseAddress) : base(byteReaderWriter, baseAddress) {
    }

    public string Command {
        get => GetZeroTerminatedString(BaseAddress, MaxCharacterLength);
        set => SetZeroTerminatedString(BaseAddress, value, MaxCharacterLength);
    }

    public const int MaxCharacterLength = 128;
}
