namespace Spice86.Shared.Interfaces; 

/// <summary>
/// Interface from the PIT available to the GUI.
/// </summary>
public interface ITimeMultiplier {
    /// <summary>
    /// Makes time go faster, or at a normal speed (value: 1)
    /// </summary>
    /// <param name="value">The value by which time will be multiplied</param>
    void SetTimeMultiplier(double value);
}