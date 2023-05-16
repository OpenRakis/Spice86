namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.OperatingSystem.Enums;
/// <summary>
/// Represents information about the formatting of dates, times, and numbers for a specific country.
/// </summary>
public class CountryInfo {
    /// <summary>
    /// Gets or sets the country code for the specific country, where 1 represents the United States, 2 represents Canada, and so on.
    /// </summary>
    public byte Country { get; set; } = (byte)CountryId.UnitedStates;
    
    /// <summary>
    /// Gets or sets the format of the date in the specified country.
    /// </summary>
    public byte DateFormat { get; set; }
    
    /// <summary>
    /// Gets or sets the character used to separate the components of a date in the specified country.
    /// </summary>
    public byte DateSeparator { get; set; }
    
    /// <summary>
    /// Gets or sets the format of the time in the specified country.
    /// </summary>
    public byte TimeFormat { get; set; }
    
    /// <summary>
    /// Gets or sets the character used to separate the components of a time in the specified country.
    /// </summary>
    public byte TimeSeparator { get; set; }
    
    /// <summary>
    /// Gets or sets the character used to separate thousands in a number in the specified country.
    /// </summary>
    public byte ThousandsSeparator { get; set; }
    
    /// <summary>
    /// Gets or sets the character used to separate the whole number part from the fractional part in a number in the specified country.
    /// </summary>
    public byte DecimalSeparator { get; set; }
}