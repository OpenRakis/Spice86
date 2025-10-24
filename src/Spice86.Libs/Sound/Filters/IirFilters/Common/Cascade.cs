namespace Spice86.Libs.Sound.Filters.IirFilters.Common;

using Spice86.Libs.Sound.Filters.IirFilters.Common.Layout;
using Spice86.Libs.Sound.Filters.IirFilters.Common.State;

using System.Numerics;

internal readonly struct CascadeStorage {
    internal CascadeStorage(int maxStages, Biquad[] stageArray) {
        MaxStages = maxStages;
        StageArray = stageArray;
    }

    internal int MaxStages { get; }

    internal Biquad[] StageArray { get; }
}

public class Cascade {
    private int _maxStages;
    private int _numStages;
    private Biquad[] _stageArray = [];

    internal void SetCascadeStorage(CascadeStorage storage) {
        _numStages = 0;
        _maxStages = storage.MaxStages;
        _stageArray = storage.StageArray;
    }

    internal Complex Response(double normalizedFrequency) {
        switch (normalizedFrequency) {
            case > 0.5:
                throw new ArgumentException("The normalised frequency needs to be =< 0.5.");
            case < 0.0:
                throw new ArgumentException("The normalised frequency needs to be >= 0.");
        }

        double w = 2.0 * MathEx.DoublePi * normalizedFrequency;
        var czn1 = Complex.FromPolarCoordinates(1.0, -w);
        var czn2 = Complex.FromPolarCoordinates(1.0, -2.0 * w);
        Complex numerator = Complex.One;
        Complex denominator = Complex.One;

        for (int i = 0; i < _numStages; i++) {
            Biquad stage = _stageArray[i];
            var ct = new Complex(stage.GetB0() / stage.GetA0(), 0.0);
            Complex cb = Complex.One;
            ct = MathEx.AddMul(ct, stage.GetB1() / stage.GetA0(), czn1);
            ct = MathEx.AddMul(ct, stage.GetB2() / stage.GetA0(), czn2);
            cb = MathEx.AddMul(cb, stage.GetA1() / stage.GetA0(), czn1);
            cb = MathEx.AddMul(cb, stage.GetA2() / stage.GetA0(), czn2);
            numerator *= ct;
            denominator *= cb;
        }

        return numerator / denominator;
    }

    internal IReadOnlyList<PoleZeroPair> GetPoleZeroPairs() {
        var result = new List<PoleZeroPair>(_numStages);
        for (int i = 0; i < _numStages; i++) {
            var state = new BiquadPoleState(_stageArray[i]);
            result.Add(state);
        }

        return result;
    }

    internal void ApplyScale(double scale) {
        if (_numStages < 1) {
            return;
        }

        _stageArray[0].ApplyScale(scale);
    }

    internal void SetLayout(LayoutBase prototype) {
        int numPoles = prototype.GetNumPoles();
        _numStages = (numPoles + 1) / 2;
        if (_numStages > _maxStages) {
            throw new ArgumentException("Number of stages is larger than the max stages.");
        }

        for (int i = 0; i < _maxStages; i++) {
            _stageArray[i].SetIdentity();
        }

        for (int i = 0; i < _numStages; i++) {
            PoleZeroPair pair = prototype[i];
            _stageArray[i].SetPoleZeroPair(pair);
        }

        double target = prototype.GetNormalGain();
        Complex response = Response(prototype.GetNormalW() / (2.0 * MathEx.DoublePi));
        ApplyScale(target / Complex.Abs(response));
    }
}

internal sealed class CascadeStages<TState>
    where TState : struct, ISectionState {
    private readonly Biquad[] _stages;
    private TState[] _states;

    internal CascadeStages(int maxStages) {
        _stages = new Biquad[maxStages];
        _states = new TState[maxStages];
        for (int i = 0; i < _stages.Length; i++) {
            _stages[i] = new Biquad();
        }
    }

    internal CascadeStorage GetCascadeStorage() {
        return new CascadeStorage(_stages.Length, _stages);
    }

    internal Biquad[] SnapshotStages() {
        var copy = new Biquad[_stages.Length];
        for (int i = 0; i < _stages.Length; i++) {
            Biquad source = _stages[i];
            var destination = new Biquad {
                A0 = source.A0,
                A1 = source.A1,
                A2 = source.A2,
                B0 = source.B0,
                B1 = source.B1,
                B2 = source.B2
            };
            copy[i] = destination;
        }

        return copy;
    }

    internal void Reset() {
        for (int i = 0; i < _states.Length; i++) {
            ref TState state = ref _states[i];
            state.Reset();
        }
    }

    internal void Setup(ReadOnlySpan<double> sosCoefficients) {
        if (sosCoefficients.Length != _stages.Length * 6) {
            throw new ArgumentException("SOS array must match the number of stages.");
        }

        int offset = 0;
        foreach (Biquad stage in _stages) {
            double b0 = sosCoefficients[offset++];
            double b1 = sosCoefficients[offset++];
            double b2 = sosCoefficients[offset++];
            double a0 = sosCoefficients[offset++];
            double a1 = sosCoefficients[offset++];
            double a2 = sosCoefficients[offset++];
            stage.SetCoefficients(a0, a1, a2, b0, b1, b2);
        }
    }

    internal void Setup(double[,] sosCoefficients) {
        if (sosCoefficients.GetLength(0) != _stages.Length || sosCoefficients.GetLength(1) != 6) {
            throw new ArgumentException("SOS matrix must be [stageCount,6].");
        }

        for (int i = 0; i < _stages.Length; i++) {
            double b0 = sosCoefficients[i, 0];
            double b1 = sosCoefficients[i, 1];
            double b2 = sosCoefficients[i, 2];
            double a0 = sosCoefficients[i, 3];
            double a1 = sosCoefficients[i, 4];
            double a2 = sosCoefficients[i, 5];
            _stages[i].SetCoefficients(a0, a1, a2, b0, b1, b2);
        }
    }

    internal double Filter(double input) {
        double output = input;
        for (int i = 0; i < _stages.Length; i++) {
            ref TState state = ref _states[i];
            output = state.Process(output, _stages[i]);
        }

        return output;
    }

    internal float FilterSingle(float input) {
        double sample = input;
        double filtered = Filter(sample);
        return (float)filtered;
    }
}