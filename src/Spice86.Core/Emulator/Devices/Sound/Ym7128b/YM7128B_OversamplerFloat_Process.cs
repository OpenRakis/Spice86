namespace Spice86.Core.Emulator.Devices.Sound.Ym7128b;
public ref struct YM7128B_OversamplerFloat_Process {
    private ref YM7128B_OversamplerFloat _self;

    public YM7128B_OversamplerFloat_Process() {
        _self = new();
    }
    public YM7128B_OversamplerFloat Self {
        get { return _self; }
        set { _self = value; }
    }

    public double Input { get; set; }
}
