namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;

/// <summary>
/// Represents a DOS environment block. <br/>
/// Stored in memory, it contains environment variables.
/// </summary>
/// <summary>
/// Abstract class providing access to environment variables
/// </summary>
public abstract class DosEnvironmentBlock : MemoryBasedDataStructure {
    protected DosEnvironmentBlock(IByteReaderWriter byteReaderWriter, uint baseAddress)
        : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// Gets the value of an environment variable
    /// </summary>
    /// <param name="entry">The name of the environment variable to retrieve</param>
    /// <returns>The value of the environment variable if found; otherwise, null</returns>
    public abstract string? GetEnvironmentValue(string entry);
}
