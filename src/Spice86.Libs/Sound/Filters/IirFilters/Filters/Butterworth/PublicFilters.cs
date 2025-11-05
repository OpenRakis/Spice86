namespace Spice86.Libs.Sound.Filters.IirFilters.Filters.Butterworth;

using Spice86.Libs.Sound.Filters.IirFilters.Common;
using Spice86.Libs.Sound.Filters.IirFilters.Common.State;

public class LowPass<TState>(int maxOrder = Constants.DefaultFilterOrder) : ButterworthLowPassBase<TState>(maxOrder)
    where TState : struct, ISectionState {
    private readonly int _maxOrder = maxOrder;

    public void Setup(double sampleRate, double cutoffFrequency) {
        base.Setup(_maxOrder, cutoffFrequency / sampleRate);
    }

    public void Setup(int order, double sampleRate, double cutoffFrequency) {
        ValidateOrder(order);
        base.Setup(order, cutoffFrequency / sampleRate);
    }

    public void SetupN(double cutoffFrequency) {
        base.Setup(_maxOrder, cutoffFrequency);
    }

    public void SetupN(int order, double cutoffFrequency) {
        ValidateOrder(order);
        base.Setup(order, cutoffFrequency);
    }
}

public sealed class LowPass(int maxOrder = Constants.DefaultFilterOrder) : LowPass<DirectFormIiState>(maxOrder);

public class HighPass<TState>(int maxOrder = Constants.DefaultFilterOrder) : ButterworthHighPassBase<TState>(maxOrder)
    where TState : struct, ISectionState {
    private readonly int _maxOrder = maxOrder;

    public void Setup(double sampleRate, double cutoffFrequency) {
        base.Setup(_maxOrder, cutoffFrequency / sampleRate);
    }

    public void Setup(int order, double sampleRate, double cutoffFrequency) {
        ValidateOrder(order);
        base.Setup(order, cutoffFrequency / sampleRate);
    }

    public void SetupN(double cutoffFrequency) {
        base.Setup(_maxOrder, cutoffFrequency);
    }

    public void SetupN(int order, double cutoffFrequency) {
        ValidateOrder(order);
        base.Setup(order, cutoffFrequency);
    }
}

public sealed class HighPass(int maxOrder = Constants.DefaultFilterOrder) : HighPass<DirectFormIiState>(maxOrder);

public class BandPass<TState>(int maxOrder = Constants.DefaultFilterOrder) : ButterworthBandPassBase<TState>(maxOrder)
    where TState : struct, ISectionState {
    private readonly int _maxOrder = maxOrder;

    public void Setup(double sampleRate, double centerFrequency, double widthFrequency) {
        base.Setup(_maxOrder, centerFrequency / sampleRate, widthFrequency / sampleRate);
    }

    public void Setup(int order, double sampleRate, double centerFrequency, double widthFrequency) {
        ValidateOrder(order);
        base.Setup(order, centerFrequency / sampleRate, widthFrequency / sampleRate);
    }

    public void SetupN(double centerFrequency, double widthFrequency) {
        base.Setup(_maxOrder, centerFrequency, widthFrequency);
    }

    public void SetupN(int order, double centerFrequency, double widthFrequency) {
        ValidateOrder(order);
        base.Setup(order, centerFrequency, widthFrequency);
    }
}

public sealed class BandPass(int maxOrder = Constants.DefaultFilterOrder) : BandPass<DirectFormIiState>(maxOrder);

public class BandStop<TState>(int maxOrder = Constants.DefaultFilterOrder) : ButterworthBandStopBase<TState>(maxOrder)
    where TState : struct, ISectionState {
    private readonly int _maxOrder = maxOrder;

    public void Setup(double sampleRate, double centerFrequency, double widthFrequency) {
        base.Setup(_maxOrder, centerFrequency / sampleRate, widthFrequency / sampleRate);
    }

    public void Setup(int order, double sampleRate, double centerFrequency, double widthFrequency) {
        ValidateOrder(order);
        base.Setup(order, centerFrequency / sampleRate, widthFrequency / sampleRate);
    }

    public void SetupN(double centerFrequency, double widthFrequency) {
        base.Setup(_maxOrder, centerFrequency, widthFrequency);
    }

    public void SetupN(int order, double centerFrequency, double widthFrequency) {
        ValidateOrder(order);
        base.Setup(order, centerFrequency, widthFrequency);
    }
}

public sealed class BandStop(int maxOrder = Constants.DefaultFilterOrder) : BandStop<DirectFormIiState>(maxOrder);

public class LowShelf<TState>(int maxOrder = Constants.DefaultFilterOrder) : ButterworthLowShelfBase<TState>(maxOrder)
    where TState : struct, ISectionState {
    private readonly int _maxOrder = maxOrder;

    public void Setup(double sampleRate, double cutoffFrequency, double gainDb) {
        base.Setup(_maxOrder, cutoffFrequency / sampleRate, gainDb);
    }

    public void Setup(int order, double sampleRate, double cutoffFrequency, double gainDb) {
        ValidateOrder(order);
        base.Setup(order, cutoffFrequency / sampleRate, gainDb);
    }

    public void SetupN(double cutoffFrequency, double gainDb) {
        base.Setup(_maxOrder, cutoffFrequency, gainDb);
    }

    public void SetupN(int order, double cutoffFrequency, double gainDb) {
        ValidateOrder(order);
        base.Setup(order, cutoffFrequency, gainDb);
    }
}

public sealed class LowShelf(int maxOrder = Constants.DefaultFilterOrder) : LowShelf<DirectFormIiState>(maxOrder);

public class HighShelf<TState>(int maxOrder = Constants.DefaultFilterOrder) : ButterworthHighShelfBase<TState>(maxOrder)
    where TState : struct, ISectionState {
    private readonly int _maxOrder = maxOrder;

    public void Setup(double sampleRate, double cutoffFrequency, double gainDb) {
        base.Setup(_maxOrder, cutoffFrequency / sampleRate, gainDb);
    }

    public void Setup(int order, double sampleRate, double cutoffFrequency, double gainDb) {
        ValidateOrder(order);
        base.Setup(order, cutoffFrequency / sampleRate, gainDb);
    }

    public void SetupN(double cutoffFrequency, double gainDb) {
        base.Setup(_maxOrder, cutoffFrequency, gainDb);
    }

    public void SetupN(int order, double cutoffFrequency, double gainDb) {
        ValidateOrder(order);
        base.Setup(order, cutoffFrequency, gainDb);
    }
}

public sealed class HighShelf(int maxOrder = Constants.DefaultFilterOrder) : HighShelf<DirectFormIiState>(maxOrder);

public class BandShelf<TState>(int maxOrder = Constants.DefaultFilterOrder) : ButterworthBandShelfBase<TState>(maxOrder)
    where TState : struct, ISectionState {
    private readonly int _maxOrder = maxOrder;

    public void Setup(double sampleRate, double centerFrequency, double widthFrequency, double gainDb) {
        base.Setup(_maxOrder, centerFrequency / sampleRate, widthFrequency / sampleRate, gainDb);
    }

    public void Setup(int order, double sampleRate, double centerFrequency, double widthFrequency, double gainDb) {
        ValidateOrder(order);
        base.Setup(order, centerFrequency / sampleRate, widthFrequency / sampleRate, gainDb);
    }

    public void SetupN(double centerFrequency, double widthFrequency, double gainDb) {
        base.Setup(_maxOrder, centerFrequency, widthFrequency, gainDb);
    }

    public void SetupN(int order, double centerFrequency, double widthFrequency, double gainDb) {
        ValidateOrder(order);
        base.Setup(order, centerFrequency, widthFrequency, gainDb);
    }
}

public sealed class BandShelf(int maxOrder = Constants.DefaultFilterOrder) : BandShelf<DirectFormIiState>(maxOrder);