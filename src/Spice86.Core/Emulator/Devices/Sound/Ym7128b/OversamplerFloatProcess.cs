namespace Spice86.Core.Emulator.Devices.Sound.Ym7128b;
public ref struct OversamplerFloatProcess {
    public OversamplerFloatProcess() {
        Self = new();
    }
    public OversamplerFloat Self { get; set; }

    public double Input { get; set; }
}
