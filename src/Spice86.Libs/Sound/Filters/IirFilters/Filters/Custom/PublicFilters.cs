namespace Spice86.Libs.Sound.Filters.IirFilters.Filters.Custom;

using Spice86.Libs.Sound.Filters.IirFilters.Common;
using Spice86.Libs.Sound.Filters.IirFilters.Common.State;

public class OnePole<TState>
    where TState : struct, ISectionState {
    private readonly OnePoleInternal<TState> _impl = new();

    public Biquad Coefficients => _impl.Coefficients;

    public void Setup(double scale, double pole, double zero) {
        _impl.Setup(scale, pole, zero);
    }

    public void Reset() {
        _impl.Reset();
    }

    public double Filter(double sample) {
        return _impl.Filter(sample);
    }

    public float Filter(float sample) {
        return _impl.Filter(sample);
    }

    public double Filter(double sample, ref TState state) {
        return _impl.Filter(sample, ref state);
    }
}

public sealed class OnePole : OnePole<DirectFormIiState>;

public class TwoPole<TState>
    where TState : struct, ISectionState {
    private readonly TwoPoleInternal<TState> _impl = new();

    public Biquad Coefficients => _impl.Coefficients;

    public void Setup(double scale, double poleRho, double poleTheta, double zeroRho, double zeroTheta) {
        _impl.Setup(scale, poleRho, poleTheta, zeroRho, zeroTheta);
    }

    public void Reset() {
        _impl.Reset();
    }

    public double Filter(double sample) {
        return _impl.Filter(sample);
    }

    public float Filter(float sample) {
        return _impl.Filter(sample);
    }

    public double Filter(double sample, ref TState state) {
        return _impl.Filter(sample, ref state);
    }
}

public sealed class TwoPole : TwoPole<DirectFormIiState>;

public class SosCascade<TState>
    where TState : struct, ISectionState {
    private readonly SosCascadeInternal<TState> _impl;

    protected SosCascade(int stageCount) {
        _impl = new SosCascadeInternal<TState>(stageCount);
    }

    protected SosCascade(double[,] sosCoefficients) {
        int stageCount = sosCoefficients.GetLength(0);
        _impl = new SosCascadeInternal<TState>(stageCount);
        _impl.Setup(sosCoefficients);
    }

    public int StageCount => _impl.StageCount;

    public void Reset() {
        _impl.Reset();
    }

    public double Filter(double sample) {
        return _impl.Filter(sample);
    }

    public float Filter(float sample) {
        return _impl.Filter(sample);
    }

    public void Setup(ReadOnlySpan<double> sosCoefficients) {
        _impl.Setup(sosCoefficients);
    }

    public void Setup(double[,] sosCoefficients) {
        _impl.Setup(sosCoefficients);
    }

    public double[,] GetCoefficientSnapshot() {
        return _impl.GetCoefficientSnapshot();
    }
}

public sealed class SosCascade : SosCascade<DirectFormIiState> {
    public SosCascade(int stageCount) : base(stageCount) { }

    public SosCascade(double[,] sosCoefficients) : base(sosCoefficients) { }
}