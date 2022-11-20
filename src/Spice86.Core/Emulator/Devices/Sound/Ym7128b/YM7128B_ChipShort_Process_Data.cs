namespace Spice86.Core.Emulator.Devices.Sound.Ym7128b;
public struct YM7128B_ChipShort_Process_Data {
    public YM7128B_ChipShort_Process_Data() {
        Inputs = new short[(int)YM7128B_InputChannel.YM7128B_InputChannel_Count];
        Outputs = new short[(int)YM7128B_OutputChannel.YM7128B_OutputChannel_Count];
    }

    public short[] Inputs { get; set; }
    public short[] Outputs { get; set; }
}