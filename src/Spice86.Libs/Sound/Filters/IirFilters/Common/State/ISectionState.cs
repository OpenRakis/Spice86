namespace Spice86.Libs.Sound.Filters.IirFilters.Common.State;

/// <summary>
/// Interface for filter section state management.
/// </summary>
public interface ISectionState {
    /// <summary>
    /// Resets the section state to its initial values.
    /// </summary>
    void Reset();

    /// <summary>
    /// Processes an input sample using the specified biquad coefficients.
    /// </summary>
    /// <param name="input">The input sample to process.</param>
    /// <param name="coefficients">The biquad filter coefficients.</param>
    /// <returns>The processed output sample.</returns>
    double Process(double input, Biquad coefficients);
}