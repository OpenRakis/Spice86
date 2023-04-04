namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.OperatingSystem.Enums;

public class CountryInfo {
    public byte Country { get; set; } = (byte)CountryId.UnitedStates;
    public byte DateFormat { get; set; }
    public byte DateSeparator { get; set; }
    public byte TimeFormat { get; set; }
    public byte TimeSeparator { get; set; }
    public byte ThousandsSeparator { get; set; }
    public byte DecimalSeparator { get; set; }
}