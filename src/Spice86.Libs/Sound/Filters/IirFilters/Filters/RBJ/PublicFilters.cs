namespace Spice86.Libs.Sound.Filters.IirFilters.Filters.RBJ;

using Spice86.Libs.Sound.Filters.IirFilters.Common.State;

public class LowPass<TState> : RbjFilterBase<TState>
    where TState : struct, ISectionState {
    public void Setup(double sampleRate, double cutoffFrequency) {
        SetupLowPass(cutoffFrequency / sampleRate, OneOverSqrtTwo);
    }

    public void Setup(double sampleRate, double cutoffFrequency, double q) {
        SetupLowPass(cutoffFrequency / sampleRate, q);
    }

    public void SetupN(double cutoffFrequency) {
        SetupLowPass(cutoffFrequency, OneOverSqrtTwo);
    }

    public void SetupN(double cutoffFrequency, double q) {
        SetupLowPass(cutoffFrequency, q);
    }
}

public sealed class LowPass : LowPass<DirectFormIiState>;

public class HighPass<TState> : RbjFilterBase<TState>
    where TState : struct, ISectionState {
    public void Setup(double sampleRate, double cutoffFrequency) {
        SetupHighPass(cutoffFrequency / sampleRate, OneOverSqrtTwo);
    }


    public void Setup(double sampleRate, double cutoffFrequency, double q) {
        SetupHighPass(cutoffFrequency / sampleRate, q);
    }

    public void SetupN(double cutoffFrequency) {
        SetupHighPass(cutoffFrequency, OneOverSqrtTwo);
    }

    public void SetupN(double cutoffFrequency, double q) {
        SetupHighPass(cutoffFrequency, q);
    }
}

public sealed class HighPass : HighPass<DirectFormIiState>;

public class BandPass1<TState> : RbjFilterBase<TState>
    where TState : struct, ISectionState {
    public void Setup(double sampleRate, double centerFrequency, double bandWidth) {
        SetupBandPass1(centerFrequency / sampleRate, bandWidth);
    }

    public void SetupN(double centerFrequency, double bandWidth) {
        SetupBandPass1(centerFrequency, bandWidth);
    }
}

public sealed class BandPass1 : BandPass1<DirectFormIiState>;

public class BandPass2<TState> : RbjFilterBase<TState>
    where TState : struct, ISectionState {
    public void Setup(double sampleRate, double centerFrequency, double bandWidth) {
        SetupBandPass2(centerFrequency / sampleRate, bandWidth);
    }

    public void SetupN(double centerFrequency, double bandWidth) {
        SetupBandPass2(centerFrequency, bandWidth);
    }
}

public sealed class BandPass2 : BandPass2<DirectFormIiState>;

public class BandStop<TState> : RbjFilterBase<TState>
    where TState : struct, ISectionState {
    public void Setup(double sampleRate, double centerFrequency, double bandWidth) {
        SetupBandStop(centerFrequency / sampleRate, bandWidth);
    }

    public void SetupN(double centerFrequency, double bandWidth) {
        SetupBandStop(centerFrequency, bandWidth);
    }
}

public sealed class BandStop : BandStop<DirectFormIiState>;

public class IirNotch<TState> : RbjFilterBase<TState>
    where TState : struct, ISectionState {
    public void Setup(double sampleRate, double centerFrequency, double qFactor = 10.0) {
        SetupNotch(centerFrequency / sampleRate, qFactor);
    }

    public void SetupN(double centerFrequency, double qFactor = 10.0) {
        SetupNotch(centerFrequency, qFactor);
    }
}

public sealed class IirNotch : IirNotch<DirectFormIiState>;

public class LowShelf<TState> : RbjFilterBase<TState>
    where TState : struct, ISectionState {
    public void Setup(double sampleRate, double cutoffFrequency, double gainDb, double shelfSlope = 1.0) {
        SetupLowShelf(cutoffFrequency / sampleRate, gainDb, shelfSlope);
    }

    public void SetupN(double cutoffFrequency, double gainDb, double shelfSlope = 1.0) {
        SetupLowShelf(cutoffFrequency, gainDb, shelfSlope);
    }
}

public sealed class LowShelf : LowShelf<DirectFormIiState>;

public class HighShelf<TState> : RbjFilterBase<TState>
    where TState : struct, ISectionState {
    public void Setup(double sampleRate, double cutoffFrequency, double gainDb, double shelfSlope = 1.0) {
        SetupHighShelf(cutoffFrequency / sampleRate, gainDb, shelfSlope);
    }

    public void SetupN(double cutoffFrequency, double gainDb, double shelfSlope = 1.0) {
        SetupHighShelf(cutoffFrequency, gainDb, shelfSlope);
    }
}

public sealed class HighShelf : HighShelf<DirectFormIiState>;

public class BandShelf<TState> : RbjFilterBase<TState>
    where TState : struct, ISectionState {
    public void Setup(double sampleRate, double centerFrequency, double gainDb, double bandWidth) {
        SetupBandShelf(centerFrequency / sampleRate, gainDb, bandWidth);
    }

    public void SetupN(double centerFrequency, double gainDb, double bandWidth) {
        SetupBandShelf(centerFrequency, gainDb, bandWidth);
    }
}

public sealed class BandShelf : BandShelf<DirectFormIiState>;

public class AllPass<TState> : RbjFilterBase<TState>
    where TState : struct, ISectionState {
    public void Setup(double sampleRate, double phaseFrequency) {
        SetupAllPass(phaseFrequency / sampleRate, OneOverSqrtTwo);
    }


    public void Setup(double sampleRate, double phaseFrequency, double q) {
        SetupAllPass(phaseFrequency / sampleRate, q);
    }

    public void SetupN(double phaseFrequency) {
        SetupAllPass(phaseFrequency, OneOverSqrtTwo);
    }

    public void SetupN(double phaseFrequency, double q) {
        SetupAllPass(phaseFrequency, q);
    }
}

public sealed class AllPass : AllPass<DirectFormIiState>;