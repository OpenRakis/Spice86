namespace Spice86.Libs.Sound.Filters.IirFilters.Common.State;

public interface ISectionState {
    void Reset();

    double Process(double input, Biquad coefficients);
}