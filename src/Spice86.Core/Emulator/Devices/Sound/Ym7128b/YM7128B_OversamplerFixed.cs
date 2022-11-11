namespace Spice86.Core.Emulator.Devices.Sound.Ym7128b;
struct YM7128B_OversamplerFixed {
    public YM7128B_OversamplerFixed() {
        buffer_ = new short[(int)YM7128B_OversamplerSpecs.YM7128B_Oversampler_Length];
    }
    short[] buffer_;
}
