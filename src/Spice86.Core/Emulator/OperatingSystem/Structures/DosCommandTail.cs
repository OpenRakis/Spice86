namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure.Array;

public class DosCommandTail : MemoryBasedDataStructure {
    public DosCommandTail(IByteReaderWriter byteReaderWriter, uint baseAddress) : base(byteReaderWriter, baseAddress) {
    }

    public UInt8Array Command => GetUInt8Array(BaseAddress, MaxCharacterLength);

    public const int MaxCharacterLength = 128;
}
