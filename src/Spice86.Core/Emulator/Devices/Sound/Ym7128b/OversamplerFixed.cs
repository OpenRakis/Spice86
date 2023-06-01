namespace Spice86.Core.Emulator.Devices.Sound.Ym7128b;
public struct OversamplerFixed {
    public OversamplerFixed() {
        Buffer = new short[(int)OversamplerSpecs.Length];
    }
    public short[] Buffer {get; set;}
}
