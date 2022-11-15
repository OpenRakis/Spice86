namespace Spice86.Core.Emulator.Devices.Sound.Ym7128b;
public struct YM7128B_OversamplerFixed {
    public YM7128B_OversamplerFixed() {
        Buffer_ = new short[(int)YM7128B_OversamplerSpecs.YM7128B_Oversampler_Length];
    }
    public short[] Buffer_ {get; set;}
}
