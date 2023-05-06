namespace Spice86.Models.Performance;

public readonly record struct Measurement {
    public double Time { get; init; }
    public double Value { get; init; }

    public override string ToString()
    {
        return $"{Time:#0.0} {Value:##0.0}";
    }
}