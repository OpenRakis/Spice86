namespace Spice86.Core.Emulator.Devices.Sound.Ym7128b;

public struct YM7128B_ChipIdeal_Process_Data {

    public YM7128B_ChipIdeal_Process_Data() {
        Inputs = new float[(int)YM7128B_InputChannel.YM7128B_InputChannel_Count];
        Outputs = new float[(int)YM7128B_OutputChannel.YM7128B_OutputChannel_Count];
    }

    public float[] Inputs { get; set; }
    public float[] Outputs { get; set; }
}