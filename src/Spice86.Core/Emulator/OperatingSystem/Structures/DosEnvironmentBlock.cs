namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;

public abstract class DosEnvironmentBlock : MemoryBasedDataStructure {
    protected DosEnvironmentBlock(IByteReaderWriter byteReaderWriter, uint baseAddress) : base(byteReaderWriter, baseAddress) {
    }

    public abstract string? GetEnvironmentVariable(string variableName);

    public abstract void SetEnvironmentVariable(string variableName, string value);
}
