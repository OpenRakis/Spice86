namespace Spice86.Core.Emulator.OperatingSystem.Structures;

/// <summary>
/// Centralizes global DOS memory structures
/// </summary>
public class DosTables {
    /// <summary>
    /// The current country information
    /// </summary>
    public CountryInfo CountryInfo { get; set; } = new();
}