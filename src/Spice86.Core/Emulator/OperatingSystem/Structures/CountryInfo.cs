namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;

using System.Diagnostics;

/// <summary>
/// Represents information about the formatting of dates, times, and numbers for a specific country.
/// </summary>
[DebuggerDisplay("{CountryId}")]
public class CountryInfo : MemoryBasedDataStructure {

    public CountryInfo(IByteReaderWriter byteReaderWriter) : base(byteReaderWriter, 0) {
    }

    /// <summary>
    /// Gets or sets the country code for the specific country, where 1 represents the United States, 2 represents Canada, and so on.
    /// </summary>
    public byte Country { get => UInt8[0]; set => UInt8[0] = value; }

    public CountryId CountryId => (CountryId)Country;

    /// <summary>
    /// Gets or sets the format of the date in the specified country.
    /// </summary>
    public byte DateFormat { get => UInt8[1]; set => UInt8[1] = value; }

    /// <summary>
    /// Gets or sets the currency string for the specified country.
    /// </summary>
    public string CurrencyString { get => GetZeroTerminatedString(2, 5); set => SetZeroTerminatedString(2, value, 5); }

    /// <summary>
    /// Gets or sets the thousands separator for the specified country.
    /// </summary>
    public byte ThousandsSeparator { get => UInt8[8]; set => UInt8[8] = value; }

    /// <summary>
    /// Gets or sets the decimal separator for the specified country.
    /// </summary>
    public byte DecimalSeparator { get => UInt8[9]; set => UInt8[9] = value; }

    /// <summary>
    /// Gets or sets the date separator for the specified country.
    /// </summary>
    public byte DateSeparator { get => UInt8[10]; set => UInt8[10] = value; }

    /// <summary>
    /// Gets or sets the time separator for the specified country.
    /// </summary>
    public byte TimeSeparator { get => UInt8[11]; set => UInt8[11] = value; }

    /// <summary>
    /// Gets or sets the currency format for the specified country.
    /// </summary>
    public byte CurrencyFormat { get => UInt8[12]; set => UInt8[12] = value; }

    /// <summary>
    /// Gets or sets the number of digits after the decimal for the specified country.
    /// </summary>
    public byte DigitsAfterDecimal { get => UInt8[13]; set => UInt8[13] = value; }

    /// <summary>
    /// Gets or sets the time format for the specified country.
    /// </summary>
    public byte TimeFormat { get => UInt8[14]; set => UInt8[14] = value; }

    /// <summary>
    /// Gets or sets the casemap for the specified country.
    /// </summary>
    public byte Casemap { get => UInt8[15]; set => UInt8[15] = value; }

    /// <summary>
    /// Gets or sets the data separator for the specified country.
    /// </summary>
    public byte DataSeparator { get => UInt8[19]; set => UInt8[19] = value; }

    /// <summary>
    /// Gets or sets the reserved value 1 for the specified country.
    /// </summary>
    public byte Reserved1 { get => UInt8[20]; set => UInt8[20] = value; }

    /// <summary>
    /// Gets or sets the reserved value 2 for the specified country.
    /// </summary>
    public byte Reserved2 { get => UInt8[21]; set => UInt8[21] = value; }

    /// <summary>
    /// Gets or sets the reserved value 3 for the specified country.
    /// </summary>
    public byte Reserved3 { get => UInt8[22]; set => UInt8[22] = value; }

    /// <summary>
    /// Gets or sets the reserved value 4 for the specified country.
    /// </summary>
    public byte Reserved4 { get => UInt8[23]; set => UInt8[23] = value; }

    /// <summary>
    /// Gets or sets the reserved value 5 for the specified country.
    /// </summary>
    public byte Reserved5 { get => UInt8[24]; set => UInt8[24] = value; }
}