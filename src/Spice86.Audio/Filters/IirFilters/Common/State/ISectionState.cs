namespace Spice86.Audio.Filters.IirFilters.Common.State;

using Spice86.Audio.Filters.IirFilters.Common;

public interface ISectionState {
    void Reset();

    double Process(double input, Biquad coefficients);
}