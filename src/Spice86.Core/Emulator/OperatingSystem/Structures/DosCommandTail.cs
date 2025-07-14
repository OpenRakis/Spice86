namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;

using System.ComponentModel.DataAnnotations;

public class DosCommandTail : MemoryBasedDataStructure {
    public DosCommandTail(IByteReaderWriter byteReaderWriter, uint baseAddress) : base(byteReaderWriter, baseAddress) {
    }

    public byte Length {
        get => UInt8[0x0];
        set => UInt8[0x0] = value;
    }

    [Range(0, MaxCharacterLength)]
    public string Command {
        get => GetZeroTerminatedString(0x1, Length);
        set {
            if (value.Length > MaxCharacterLength) {
                throw new ArgumentException($"Command length cannot exceed {MaxCharacterLength} characters.");
            }
            SetZeroTerminatedString(0x1, value, Length);
        }
    }

    public const int MaxCharacterLength = 128;

    public const int OffsetInPspSegment = 0x80;
}
