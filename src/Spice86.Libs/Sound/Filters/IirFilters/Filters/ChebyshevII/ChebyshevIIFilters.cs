namespace Spice86.Libs.Sound.Filters.IirFilters.Filters.ChebyshevII;

using Spice86.Libs.Sound.Filters.IirFilters.Common;
using Spice86.Libs.Sound.Filters.IirFilters.Common.Layout;
using Spice86.Libs.Sound.Filters.IirFilters.Common.State;
using Spice86.Libs.Sound.Filters.IirFilters.Common.Transforms;

public abstract class ChebyshevIiFilterBase<TAnalog, TState> : PoleFilterBase<TAnalog, TState>
    where TAnalog : LayoutBase
    where TState : struct, ISectionState {
    protected ChebyshevIiFilterBase(int maxOrder, int maxDigitalPoles, TAnalog analog)
        : base(maxOrder, maxDigitalPoles, analog) {
        MaxOrder = maxOrder;
    }

    protected int MaxOrder { get; }

    protected void ValidateOrder(int order) {
        if (order > MaxOrder) {
            throw new ArgumentException(Constants.OrderTooHigh);
        }
    }
}

public abstract class ChebyshevIiLowPassBase<TState> : ChebyshevIiFilterBase<AnalogLowPass, TState>
    where TState : struct, ISectionState {
    protected ChebyshevIiLowPassBase(int maxOrder)
        : base(maxOrder, maxOrder, new AnalogLowPass()) {
    }

    protected void Setup(int order, double normalizedCutoff, double stopBandDb) {
        ValidateOrder(order);
        AnalogPrototype.Design(order, stopBandDb);
        _ = new LowPassTransform(normalizedCutoff, DigitalPrototype, AnalogPrototype);
        SetLayout(DigitalPrototype);
    }
}

public abstract class ChebyshevIiHighPassBase<TState> : ChebyshevIiFilterBase<AnalogLowPass, TState>
    where TState : struct, ISectionState {
    protected ChebyshevIiHighPassBase(int maxOrder)
        : base(maxOrder, maxOrder, new AnalogLowPass()) {
    }

    protected void Setup(int order, double normalizedCutoff, double stopBandDb) {
        ValidateOrder(order);
        AnalogPrototype.Design(order, stopBandDb);
        _ = new HighPassTransform(normalizedCutoff, DigitalPrototype, AnalogPrototype);
        SetLayout(DigitalPrototype);
    }
}

public abstract class ChebyshevIiBandPassBase<TState> : ChebyshevIiFilterBase<AnalogLowPass, TState>
    where TState : struct, ISectionState {
    protected ChebyshevIiBandPassBase(int maxOrder)
        : base(maxOrder, maxOrder * 2, new AnalogLowPass()) {
    }

    protected void Setup(int order, double centerFrequency, double widthFrequency, double stopBandDb) {
        ValidateOrder(order);
        AnalogPrototype.Design(order, stopBandDb);
        _ = new BandPassTransform(centerFrequency, widthFrequency, DigitalPrototype, AnalogPrototype);
        SetLayout(DigitalPrototype);
    }
}

public abstract class ChebyshevIiBandStopBase<TState> : ChebyshevIiFilterBase<AnalogLowPass, TState>
    where TState : struct, ISectionState {
    protected ChebyshevIiBandStopBase(int maxOrder)
        : base(maxOrder, maxOrder * 2, new AnalogLowPass()) {
    }

    protected void Setup(int order, double centerFrequency, double widthFrequency, double stopBandDb) {
        ValidateOrder(order);
        AnalogPrototype.Design(order, stopBandDb);
        _ = new BandStopTransform(centerFrequency, widthFrequency, DigitalPrototype, AnalogPrototype);
        SetLayout(DigitalPrototype);
    }
}

public abstract class ChebyshevIiLowShelfBase<TState> : ChebyshevIiFilterBase<AnalogLowShelf, TState>
    where TState : struct, ISectionState {
    protected ChebyshevIiLowShelfBase(int maxOrder)
        : base(maxOrder, maxOrder, new AnalogLowShelf()) {
    }

    protected void Setup(int order, double normalizedCutoff, double gainDb, double stopBandDb) {
        ValidateOrder(order);
        AnalogPrototype.Design(order, gainDb, stopBandDb);
        _ = new LowPassTransform(normalizedCutoff, DigitalPrototype, AnalogPrototype);
        SetLayout(DigitalPrototype);
    }
}

public abstract class ChebyshevIiHighShelfBase<TState> : ChebyshevIiFilterBase<AnalogLowShelf, TState>
    where TState : struct, ISectionState {
    protected ChebyshevIiHighShelfBase(int maxOrder)
        : base(maxOrder, maxOrder, new AnalogLowShelf()) {
    }

    protected void Setup(int order, double normalizedCutoff, double gainDb, double stopBandDb) {
        ValidateOrder(order);
        AnalogPrototype.Design(order, gainDb, stopBandDb);
        _ = new HighPassTransform(normalizedCutoff, DigitalPrototype, AnalogPrototype);
        SetLayout(DigitalPrototype);
    }
}

public abstract class ChebyshevIiBandShelfBase<TState> : ChebyshevIiFilterBase<AnalogLowShelf, TState>
    where TState : struct, ISectionState {
    protected ChebyshevIiBandShelfBase(int maxOrder)
        : base(maxOrder, maxOrder * 2, new AnalogLowShelf()) {
    }

    protected void Setup(int order, double centerFrequency, double widthFrequency, double gainDb, double stopBandDb) {
        ValidateOrder(order);
        AnalogPrototype.Design(order, gainDb, stopBandDb);
        _ = new BandPassTransform(centerFrequency, widthFrequency, DigitalPrototype, AnalogPrototype);
        DigitalPrototype.SetNormal(centerFrequency < 0.25 ? MathEx.DoublePi : 0.0, 1.0);
        SetLayout(DigitalPrototype);
    }
}