namespace Spice86.Libs.Sound.Filters.IirFilters.Common;

using Spice86.Libs.Sound.Filters.IirFilters.Common.Layout;
using Spice86.Libs.Sound.Filters.IirFilters.Common.State;

public abstract class PoleFilterBase<TAnalog, TState> : Cascade
    where TAnalog : LayoutBase
    where TState : struct, ISectionState {
    private readonly CascadeStages<TState> _stages;

    protected PoleFilterBase(int maxAnalogPoles, int maxDigitalPoles, TAnalog analogPrototype) {
        var analogStorage = new LayoutStorage(maxAnalogPoles);
        var digitalStorage = new LayoutStorage(maxDigitalPoles);

        AnalogPrototype = analogPrototype;
        AnalogPrototype.SetStorage(analogStorage.Base);
        DigitalPrototype = digitalStorage.Base;

        _stages = new CascadeStages<TState>((maxDigitalPoles + 1) / 2);
        SetCascadeStorage(_stages.GetCascadeStorage());
        _stages.Reset();
    }

    protected TAnalog AnalogPrototype { get; }

    protected LayoutBase DigitalPrototype { get; }

    public void Reset() {
        _stages.Reset();
    }

    public double Filter(double input) {
        return _stages.Filter(input);
    }

    public float Filter(float input) {
        return _stages.FilterSingle(input);
    }
}