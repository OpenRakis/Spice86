namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;

/// <summary>
/// Represents a DOS environment block. <br/>
/// Stored in memory, it contains information variables.
/// </summary>
public abstract class DosEnvironmentBlock : MemoryBasedDataStructure {

    /// <summary>
    /// Initializes a new instance of the <see cref="DosEnvironmentBlock"/> class. <br/>
    /// </summary>
    /// <param name="byteReaderWriter">The main memory bus.</param>
    /// <param name="baseAddress">The base address of the data structure.</param>
    protected DosEnvironmentBlock(IByteReaderWriter byteReaderWriter, uint baseAddress)
        : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// Gets the environment variable with the specified name. <br/>
    /// </summary>
    /// <param name="variableName">The unique string identifying the environment variable.</param>
    /// <returns>The string value corresponding to the environment variable or <c>null</c> if not found.</returns>
    public abstract string? GetEnvironmentVariable(string variableName);

    /// <summary>
    /// Sets the environment variable with the specified name. <br/>
    /// </summary>
    /// <param name="variableName">The unique string identifying the environment variable.</param>
    /// <param name="value">The string value to store in memory.</param>
    public abstract void SetEnvironmentVariable(string variableName, string value);
}
