namespace Spice86.Models.Performance;

/// <summary>
/// Represents a performance measurement with a number and value.
/// </summary>
public readonly record struct Measurement {
    public double Number { get; init; }

    public double Value { get; init; }

    /// <summary>
    /// Returns a string representation of the measurement in the format "number value".
    /// </summary>
    /// <returns>A string representation of the measurement.</returns>
    public override string ToString()
    {
        return $"{Number:#0.0} {Value:##0.0}";
    }
}
