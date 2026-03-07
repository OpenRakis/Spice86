namespace Spice86.Libs.Sound.Filters.IirFilters.Filters.ChebyshevI;

using Spice86.Libs.Sound.Filters.IirFilters.Common;
using Spice86.Libs.Sound.Filters.IirFilters.Common.State;

public class LowPass<TState> : ChebyshevILowPassBase<TState>
    where TState : struct, ISectionState {
    private readonly int _maxOrder;

    protected LowPass(int maxOrder = Constants.DefaultFilterOrder)
        : base(maxOrder) {
        _maxOrder = maxOrder;
    }

    public void Setup(double sampleRate, double cutoffFrequency, double rippleDb) {
        base.Setup(_maxOrder, cutoffFrequency / sampleRate, rippleDb);
    }

    public void Setup(int order, double sampleRate, double cutoffFrequency, double rippleDb) {
        base.Setup(order, cutoffFrequency / sampleRate, rippleDb);
    }

    public void SetupN(double cutoffFrequency, double rippleDb) {
        base.Setup(_maxOrder, cutoffFrequency, rippleDb);
    }

    public void SetupN(int order, double cutoffFrequency, double rippleDb) {
        base.Setup(order, cutoffFrequency, rippleDb);
    }
}

public sealed class LowPass(int maxOrder = Constants.DefaultFilterOrder) : LowPass<DirectFormIiState>(maxOrder);

public class HighPass<TState>(int maxOrder = Constants.DefaultFilterOrder) : ChebyshevIHighPassBase<TState>(maxOrder)
    where TState : struct, ISectionState {
    private readonly int _maxOrder = maxOrder;

    public void Setup(double sampleRate, double cutoffFrequency, double rippleDb) {
        base.Setup(_maxOrder, cutoffFrequency / sampleRate, rippleDb);
    }

    public void Setup(int order, double sampleRate, double cutoffFrequency, double rippleDb) {
        base.Setup(order, cutoffFrequency / sampleRate, rippleDb);
    }

    public void SetupN(double cutoffFrequency, double rippleDb) {
        base.Setup(_maxOrder, cutoffFrequency, rippleDb);
    }

    public void SetupN(int order, double cutoffFrequency, double rippleDb) {
        base.Setup(order, cutoffFrequency, rippleDb);
    }
}

public sealed class HighPass(int maxOrder = Constants.DefaultFilterOrder) : HighPass<DirectFormIiState>(maxOrder);

public class BandPass<TState>(int maxOrder = Constants.DefaultFilterOrder) : ChebyshevIBandPassBase<TState>(maxOrder)
    where TState : struct, ISectionState {
    private readonly int _maxOrder = maxOrder;

    public void Setup(double sampleRate, double centerFrequency, double widthFrequency, double rippleDb) {
        base.Setup(_maxOrder, centerFrequency / sampleRate, widthFrequency / sampleRate, rippleDb);
    }

    public void Setup(int order, double sampleRate, double centerFrequency, double widthFrequency, double rippleDb) {
        base.Setup(order, centerFrequency / sampleRate, widthFrequency / sampleRate, rippleDb);
    }

    public void SetupN(double centerFrequency, double widthFrequency, double rippleDb) {
        base.Setup(_maxOrder, centerFrequency, widthFrequency, rippleDb);
    }

    public void SetupN(int order, double centerFrequency, double widthFrequency, double rippleDb) {
        base.Setup(order, centerFrequency, widthFrequency, rippleDb);
    }
}

public sealed class BandPass(int maxOrder = Constants.DefaultFilterOrder) : BandPass<DirectFormIiState>(maxOrder);

public class BandStop<TState>(int maxOrder = Constants.DefaultFilterOrder) : ChebyshevIBandStopBase<TState>(maxOrder)
    where TState : struct, ISectionState {
    private readonly int _maxOrder = maxOrder;

    public void Setup(double sampleRate, double centerFrequency, double widthFrequency, double rippleDb) {
        base.Setup(_maxOrder, centerFrequency / sampleRate, widthFrequency / sampleRate, rippleDb);
    }

    public void Setup(int order, double sampleRate, double centerFrequency, double widthFrequency, double rippleDb) {
        base.Setup(order, centerFrequency / sampleRate, widthFrequency / sampleRate, rippleDb);
    }

    public void SetupN(double centerFrequency, double widthFrequency, double rippleDb) {
        base.Setup(_maxOrder, centerFrequency, widthFrequency, rippleDb);
    }

    public void SetupN(int order, double centerFrequency, double widthFrequency, double rippleDb) {
        base.Setup(order, centerFrequency, widthFrequency, rippleDb);
    }
}

public sealed class BandStop(int maxOrder = Constants.DefaultFilterOrder) : BandStop<DirectFormIiState>(maxOrder);

public class LowShelf<TState>(int maxOrder = Constants.DefaultFilterOrder) : ChebyshevILowShelfBase<TState>(maxOrder)
    where TState : struct, ISectionState {
    private readonly int _maxOrder = maxOrder;

    public void Setup(double sampleRate, double cutoffFrequency, double gainDb, double rippleDb) {
        base.Setup(_maxOrder, cutoffFrequency / sampleRate, gainDb, rippleDb);
    }

    public void Setup(int order, double sampleRate, double cutoffFrequency, double gainDb, double rippleDb) {
        base.Setup(order, cutoffFrequency / sampleRate, gainDb, rippleDb);
    }

    public void SetupN(double cutoffFrequency, double gainDb, double rippleDb) {
        base.Setup(_maxOrder, cutoffFrequency, gainDb, rippleDb);
    }

    public void SetupN(int order, double cutoffFrequency, double gainDb, double rippleDb) {
        base.Setup(order, cutoffFrequency, gainDb, rippleDb);
    }
}

public sealed class LowShelf(int maxOrder = Constants.DefaultFilterOrder) : LowShelf<DirectFormIiState>(maxOrder);

public class HighShelf<TState>(int maxOrder = Constants.DefaultFilterOrder) : ChebyshevIHighShelfBase<TState>(maxOrder)
    where TState : struct, ISectionState {
    private readonly int _maxOrder = maxOrder;

    public void Setup(double sampleRate, double cutoffFrequency, double gainDb, double rippleDb) {
        base.Setup(_maxOrder, cutoffFrequency / sampleRate, gainDb, rippleDb);
    }

    public void Setup(int order, double sampleRate, double cutoffFrequency, double gainDb, double rippleDb) {
        base.Setup(order, cutoffFrequency / sampleRate, gainDb, rippleDb);
    }

    public void SetupN(double cutoffFrequency, double gainDb, double rippleDb) {
        base.Setup(_maxOrder, cutoffFrequency, gainDb, rippleDb);
    }

    public void SetupN(int order, double cutoffFrequency, double gainDb, double rippleDb) {
        base.Setup(order, cutoffFrequency, gainDb, rippleDb);
    }
}

public sealed class HighShelf(int maxOrder = Constants.DefaultFilterOrder) : HighShelf<DirectFormIiState>(maxOrder);

public class BandShelf<TState>(int maxOrder = Constants.DefaultFilterOrder) : ChebyshevIBandShelfBase<TState>(maxOrder)
    where TState : struct, ISectionState {
    private readonly int _maxOrder = maxOrder;

    public void Setup(double sampleRate, double centerFrequency, double widthFrequency, double gainDb,
        double rippleDb) {
        base.Setup(_maxOrder, centerFrequency / sampleRate, widthFrequency / sampleRate, gainDb, rippleDb);
    }

    public void Setup(int order, double sampleRate, double centerFrequency, double widthFrequency, double gainDb,
        double rippleDb) {
        base.Setup(order, centerFrequency / sampleRate, widthFrequency / sampleRate, gainDb, rippleDb);
    }

    public void SetupN(double centerFrequency, double widthFrequency, double gainDb, double rippleDb) {
        base.Setup(_maxOrder, centerFrequency, widthFrequency, gainDb, rippleDb);
    }

    public void SetupN(int order, double centerFrequency, double widthFrequency, double gainDb, double rippleDb) {
        base.Setup(order, centerFrequency, widthFrequency, gainDb, rippleDb);
    }
}

public sealed class BandShelf(int maxOrder = Constants.DefaultFilterOrder) : BandShelf<DirectFormIiState>(maxOrder);