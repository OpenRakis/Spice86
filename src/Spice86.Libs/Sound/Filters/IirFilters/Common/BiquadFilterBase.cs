namespace Spice86.Libs.Sound.Filters.IirFilters.Common;

using Spice86.Libs.Sound.Filters.IirFilters.Common.State;

public abstract class BiquadFilterBase<TState>
    where TState : struct, ISectionState {
    // ReSharper disable once RedundantDefaultMemberInitializer
    private TState _state = default;

    public Biquad Coefficients { get; } = new();

    protected void SetCoefficients(double a0, double a1, double a2, double b0, double b1, double b2) {
        Coefficients.SetCoefficients(a0, a1, a2, b0, b1, b2);
    }

    public void Reset() {
        _state.Reset();
    }

    public double Filter(double sample) {
        return _state.Process(sample, Coefficients);
    }

    public float Filter(float sample) {
        return (float)Filter((double)sample);
    }

    public double Filter(double sample, ref TState state) {
        return state.Process(sample, Coefficients);
    }
}