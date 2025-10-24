namespace Spice86.Libs.Sound.Filters.IirFilters.Common.State;

public struct DirectFormIState : ISectionState {
    private double _x1;
    private double _x2;
    private double _y1;
    private double _y2;

    public void Reset() {
        _x1 = 0.0;
        _x2 = 0.0;
        _y1 = 0.0;
        _y2 = 0.0;
    }

    public double Process(double input, Biquad section) {
        double output = (section.B0 * input) +
                        (section.B1 * _x1) +
                        (section.B2 * _x2) -
                        (section.A1 * _y1) -
                        (section.A2 * _y2);

        _x2 = _x1;
        _y2 = _y1;
        _x1 = input;
        _y1 = output;

        return output;
    }
}

public struct DirectFormIiState : ISectionState {
    private double _v1;
    private double _v2;

    public void Reset() {
        _v1 = 0.0;
        _v2 = 0.0;
    }

    public double Process(double input, Biquad section) {
        double w = input - (section.A1 * _v1) - (section.A2 * _v2);
        double output = (section.B0 * w) + (section.B1 * _v1) + (section.B2 * _v2);

        _v2 = _v1;
        _v1 = w;

        return output;
    }
}

public struct TransposedDirectFormIiState : ISectionState {
    private double _s1;
    private double _s1Prev;
    private double _s2;
    private double _s2Prev;

    public void Reset() {
        _s1 = 0.0;
        _s1Prev = 0.0;
        _s2 = 0.0;
        _s2Prev = 0.0;
    }

    public double Process(double input, Biquad section) {
        double output = _s1Prev + (section.B0 * input);
        _s1 = _s2Prev + (section.B1 * input) - (section.A1 * output);
        _s2 = (section.B2 * input) - (section.A2 * output);
        _s1Prev = _s1;
        _s2Prev = _s2;
        return output;
    }
}