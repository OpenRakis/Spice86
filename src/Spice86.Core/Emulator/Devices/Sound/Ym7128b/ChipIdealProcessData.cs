namespace Spice86.Core.Emulator.Devices.Sound.Ym7128b;

public struct ChipIdealProcessData {

    public ChipIdealProcessData() {
        Inputs = new float[(int)InputChannel.Count];
        Outputs = new float[(int)OutputChannel.Count];
    }

    public float[] Inputs { get; set; }
    public float[] Outputs { get; set; }
}