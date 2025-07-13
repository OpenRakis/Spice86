namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;

/// <summary>
/// Centralizes global DOS memory structures
/// </summary>
public class DosTables {
    /// <summary>
    /// The current country information
    /// </summary>
    public CountryInfo CountryInfo { get; init; }
    
    public DosTables(IByteReaderWriter memory) {
        CountryInfo = new(memory);
    }
}