namespace Spice86.Libs.Sound.Filters.IirFilters.Filters.ChebyshevII;

using Spice86.Libs.Sound.Filters.IirFilters.Common;
using Spice86.Libs.Sound.Filters.IirFilters.Common.State;

public class LowPass<TState>(int maxOrder = Constants.DefaultFilterOrder) : ChebyshevIiLowPassBase<TState>(maxOrder)
    where TState : struct, ISectionState {
    private readonly int _maxOrder = maxOrder;

    public void Setup(double sampleRate, double cutoffFrequency, double stopBandDb) {
        base.Setup(_maxOrder, cutoffFrequency / sampleRate, stopBandDb);
    }

    public void Setup(int order, double sampleRate, double cutoffFrequency, double stopBandDb) {
        base.Setup(order, cutoffFrequency / sampleRate, stopBandDb);
    }

    public void SetupN(double cutoffFrequency, double stopBandDb) {
        base.Setup(_maxOrder, cutoffFrequency, stopBandDb);
    }

    public void SetupN(int order, double cutoffFrequency, double stopBandDb) {
        base.Setup(order, cutoffFrequency, stopBandDb);
    }
}

public sealed class LowPass(int maxOrder = Constants.DefaultFilterOrder) : LowPass<DirectFormIiState>(maxOrder);

public class HighPass<TState>(int maxOrder = Constants.DefaultFilterOrder) : ChebyshevIiHighPassBase<TState>(maxOrder)
    where TState : struct, ISectionState {
    private readonly int _maxOrder = maxOrder;

    public void Setup(double sampleRate, double cutoffFrequency, double stopBandDb) {
        base.Setup(_maxOrder, cutoffFrequency / sampleRate, stopBandDb);
    }

    public void Setup(int order, double sampleRate, double cutoffFrequency, double stopBandDb) {
        base.Setup(order, cutoffFrequency / sampleRate, stopBandDb);
    }

    public void SetupN(double cutoffFrequency, double stopBandDb) {
        base.Setup(_maxOrder, cutoffFrequency, stopBandDb);
    }

    public void SetupN(int order, double cutoffFrequency, double stopBandDb) {
        base.Setup(order, cutoffFrequency, stopBandDb);
    }
}

public sealed class HighPass(int maxOrder = Constants.DefaultFilterOrder) : HighPass<DirectFormIiState>(maxOrder);

public class BandPass<TState>(int maxOrder = Constants.DefaultFilterOrder) : ChebyshevIiBandPassBase<TState>(maxOrder)
    where TState : struct, ISectionState {
    private readonly int _maxOrder = maxOrder;

    public void Setup(double sampleRate, double centerFrequency, double widthFrequency, double stopBandDb) {
        base.Setup(_maxOrder, centerFrequency / sampleRate, widthFrequency / sampleRate, stopBandDb);
    }

    public void Setup(int order, double sampleRate, double centerFrequency, double widthFrequency, double stopBandDb) {
        base.Setup(order, centerFrequency / sampleRate, widthFrequency / sampleRate, stopBandDb);
    }

    public void SetupN(double centerFrequency, double widthFrequency, double stopBandDb) {
        base.Setup(_maxOrder, centerFrequency, widthFrequency, stopBandDb);
    }

    public void SetupN(int order, double centerFrequency, double widthFrequency, double stopBandDb) {
        base.Setup(order, centerFrequency, widthFrequency, stopBandDb);
    }
}

public sealed class BandPass(int maxOrder = Constants.DefaultFilterOrder) : BandPass<DirectFormIiState>(maxOrder);

public class BandStop<TState>(int maxOrder = Constants.DefaultFilterOrder) : ChebyshevIiBandStopBase<TState>(maxOrder)
    where TState : struct, ISectionState {
    private readonly int _maxOrder = maxOrder;

    public void Setup(double sampleRate, double centerFrequency, double widthFrequency, double stopBandDb) {
        base.Setup(_maxOrder, centerFrequency / sampleRate, widthFrequency / sampleRate, stopBandDb);
    }

    public void Setup(int order, double sampleRate, double centerFrequency, double widthFrequency, double stopBandDb) {
        base.Setup(order, centerFrequency / sampleRate, widthFrequency / sampleRate, stopBandDb);
    }

    public void SetupN(double centerFrequency, double widthFrequency, double stopBandDb) {
        base.Setup(_maxOrder, centerFrequency, widthFrequency, stopBandDb);
    }

    public void SetupN(int order, double centerFrequency, double widthFrequency, double stopBandDb) {
        base.Setup(order, centerFrequency, widthFrequency, stopBandDb);
    }
}

public sealed class BandStop(int maxOrder = Constants.DefaultFilterOrder) : BandStop<DirectFormIiState>(maxOrder);

public class LowShelf<TState>(int maxOrder = Constants.DefaultFilterOrder) : ChebyshevIiLowShelfBase<TState>(maxOrder)
    where TState : struct, ISectionState {
    private readonly int _maxOrder = maxOrder;

    public void Setup(double sampleRate, double cutoffFrequency, double gainDb, double stopBandDb) {
        base.Setup(_maxOrder, cutoffFrequency / sampleRate, gainDb, stopBandDb);
    }

    public void Setup(int order, double sampleRate, double cutoffFrequency, double gainDb, double stopBandDb) {
        base.Setup(order, cutoffFrequency / sampleRate, gainDb, stopBandDb);
    }

    public void SetupN(double cutoffFrequency, double gainDb, double stopBandDb) {
        base.Setup(_maxOrder, cutoffFrequency, gainDb, stopBandDb);
    }

    public void SetupN(int order, double cutoffFrequency, double gainDb, double stopBandDb) {
        base.Setup(order, cutoffFrequency, gainDb, stopBandDb);
    }
}

public sealed class LowShelf(int maxOrder = Constants.DefaultFilterOrder) : LowShelf<DirectFormIiState>(maxOrder);

public class HighShelf<TState>(int maxOrder = Constants.DefaultFilterOrder) : ChebyshevIiHighShelfBase<TState>(maxOrder)
    where TState : struct, ISectionState {
    private readonly int _maxOrder = maxOrder;

    public void Setup(double sampleRate, double cutoffFrequency, double gainDb, double stopBandDb) {
        base.Setup(_maxOrder, cutoffFrequency / sampleRate, gainDb, stopBandDb);
    }

    public void Setup(int order, double sampleRate, double cutoffFrequency, double gainDb, double stopBandDb) {
        base.Setup(order, cutoffFrequency / sampleRate, gainDb, stopBandDb);
    }

    public void SetupN(double cutoffFrequency, double gainDb, double stopBandDb) {
        base.Setup(_maxOrder, cutoffFrequency, gainDb, stopBandDb);
    }

    public void SetupN(int order, double cutoffFrequency, double gainDb, double stopBandDb) {
        base.Setup(order, cutoffFrequency, gainDb, stopBandDb);
    }
}

public sealed class HighShelf(int maxOrder = Constants.DefaultFilterOrder) : HighShelf<DirectFormIiState>(maxOrder);

public class BandShelf<TState>(int maxOrder = Constants.DefaultFilterOrder) : ChebyshevIiBandShelfBase<TState>(maxOrder)
    where TState : struct, ISectionState {
    private readonly int _maxOrder = maxOrder;

    public void Setup(double sampleRate, double centerFrequency, double widthFrequency, double gainDb,
        double stopBandDb) {
        base.Setup(_maxOrder, centerFrequency / sampleRate, widthFrequency / sampleRate, gainDb, stopBandDb);
    }

    public void Setup(int order, double sampleRate, double centerFrequency, double widthFrequency, double gainDb,
        double stopBandDb) {
        base.Setup(order, centerFrequency / sampleRate, widthFrequency / sampleRate, gainDb, stopBandDb);
    }

    public void SetupN(double centerFrequency, double widthFrequency, double gainDb, double stopBandDb) {
        base.Setup(_maxOrder, centerFrequency, widthFrequency, gainDb, stopBandDb);
    }

    public void SetupN(int order, double centerFrequency, double widthFrequency, double gainDb, double stopBandDb) {
        base.Setup(order, centerFrequency, widthFrequency, gainDb, stopBandDb);
    }
}

public sealed class BandShelf(int maxOrder = Constants.DefaultFilterOrder) : BandShelf<DirectFormIiState>(maxOrder);