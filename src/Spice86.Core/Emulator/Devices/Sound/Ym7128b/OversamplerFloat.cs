namespace Spice86.Core.Emulator.Devices.Sound.Ym7128b;
public struct OversamplerFloat {
    public OversamplerFloat() {
        Buffer = new double[(int)OversamplerSpecs.Length];
    }
    public double[] Buffer { get; set; }
}
