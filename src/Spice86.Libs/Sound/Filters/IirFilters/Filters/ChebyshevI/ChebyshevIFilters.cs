namespace Spice86.Libs.Sound.Filters.IirFilters.Filters.ChebyshevI;

using Spice86.Libs.Sound.Filters.IirFilters.Common;
using Spice86.Libs.Sound.Filters.IirFilters.Common.Layout;
using Spice86.Libs.Sound.Filters.IirFilters.Common.State;
using Spice86.Libs.Sound.Filters.IirFilters.Common.Transforms;

public abstract class ChebyshevIFilterBase<TAnalog, TState>(int maxOrder, int maxDigitalPoles, TAnalog analog)
    : PoleFilterBase<TAnalog, TState>(maxOrder, maxDigitalPoles, analog)
    where TAnalog : LayoutBase
    where TState : struct, ISectionState {
    protected int MaxOrder { get; } = maxOrder;

    protected void ValidateOrder(int order) {
        if (order > MaxOrder) {
            throw new ArgumentException(Constants.OrderTooHigh);
        }
    }
}

public abstract class ChebyshevILowPassBase<TState>(int maxOrder)
    : ChebyshevIFilterBase<AnalogLowPass, TState>(maxOrder, maxOrder, new AnalogLowPass())
    where TState : struct, ISectionState {
    protected void Setup(int order, double normalizedCutoff, double rippleDb) {
        ValidateOrder(order);
        AnalogPrototype.Design(order, rippleDb);
        _ = new LowPassTransform(normalizedCutoff, DigitalPrototype, AnalogPrototype);
        SetLayout(DigitalPrototype);
    }
}

public abstract class ChebyshevIHighPassBase<TState>(int maxOrder)
    : ChebyshevIFilterBase<AnalogLowPass, TState>(maxOrder, maxOrder, new AnalogLowPass())
    where TState : struct, ISectionState {
    protected void Setup(int order, double normalizedCutoff, double rippleDb) {
        ValidateOrder(order);
        AnalogPrototype.Design(order, rippleDb);
        _ = new HighPassTransform(normalizedCutoff, DigitalPrototype, AnalogPrototype);
        SetLayout(DigitalPrototype);
    }
}

public abstract class ChebyshevIBandPassBase<TState>(int maxOrder)
    : ChebyshevIFilterBase<AnalogLowPass, TState>(maxOrder, maxOrder * 2, new AnalogLowPass())
    where TState : struct, ISectionState {
    protected void Setup(int order, double centerFrequency, double widthFrequency, double rippleDb) {
        ValidateOrder(order);
        AnalogPrototype.Design(order, rippleDb);
        _ = new BandPassTransform(centerFrequency, widthFrequency, DigitalPrototype, AnalogPrototype);
        SetLayout(DigitalPrototype);
    }
}

public abstract class ChebyshevIBandStopBase<TState>(int maxOrder)
    : ChebyshevIFilterBase<AnalogLowPass, TState>(maxOrder, maxOrder * 2, new AnalogLowPass())
    where TState : struct, ISectionState {
    protected void Setup(int order, double centerFrequency, double widthFrequency, double rippleDb) {
        ValidateOrder(order);
        AnalogPrototype.Design(order, rippleDb);
        _ = new BandStopTransform(centerFrequency, widthFrequency, DigitalPrototype, AnalogPrototype);
        SetLayout(DigitalPrototype);
    }
}

public abstract class ChebyshevILowShelfBase<TState>(int maxOrder)
    : ChebyshevIFilterBase<AnalogLowShelf, TState>(maxOrder, maxOrder, new AnalogLowShelf())
    where TState : struct, ISectionState {
    protected void Setup(int order, double normalizedCutoff, double gainDb, double rippleDb) {
        ValidateOrder(order);
        AnalogPrototype.Design(order, gainDb, rippleDb);
        _ = new LowPassTransform(normalizedCutoff, DigitalPrototype, AnalogPrototype);
        SetLayout(DigitalPrototype);
    }
}

public abstract class ChebyshevIHighShelfBase<TState>(int maxOrder)
    : ChebyshevIFilterBase<AnalogLowShelf, TState>(maxOrder, maxOrder, new AnalogLowShelf())
    where TState : struct, ISectionState {
    protected void Setup(int order, double normalizedCutoff, double gainDb, double rippleDb) {
        ValidateOrder(order);
        AnalogPrototype.Design(order, gainDb, rippleDb);
        _ = new HighPassTransform(normalizedCutoff, DigitalPrototype, AnalogPrototype);
        SetLayout(DigitalPrototype);
    }
}

public abstract class ChebyshevIBandShelfBase<TState>(int maxOrder)
    : ChebyshevIFilterBase<AnalogLowShelf, TState>(maxOrder, maxOrder * 2, new AnalogLowShelf())
    where TState : struct, ISectionState {
    protected void Setup(int order, double centerFrequency, double widthFrequency, double gainDb, double rippleDb) {
        ValidateOrder(order);
        AnalogPrototype.Design(order, gainDb, rippleDb);
        _ = new BandPassTransform(centerFrequency, widthFrequency, DigitalPrototype, AnalogPrototype);
        DigitalPrototype.SetNormal(centerFrequency < 0.25 ? MathEx.DoublePi : 0.0, 1.0);
        SetLayout(DigitalPrototype);
    }
}