namespace Spice86.Core.Emulator.Devices.Sound.Ym7128b;
public struct YM7128B_OversamplerFloat {
    public YM7128B_OversamplerFloat() {
        Buffer = new double[(int)YM7128B_OversamplerSpecs.YM7128B_Oversampler_Length];
    }
    public double[] Buffer { get; set; }
}
