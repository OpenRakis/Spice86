namespace Spice86.Core.Emulator.Devices.Sound.Ym7128b;
public struct YM7128B_ChipFloat_Process_Data {
    public YM7128B_ChipFloat_Process_Data() {
        Inputs = new double[(int)YM7128B_InputChannel.YM7128B_InputChannel_Count];
        Outputs = new double[(int)YM7128B_OutputChannel.YM7128B_OutputChannel_Count,(int)YM7128B_DatasheetSpecs.YM7128B_Oversampling];
    }

    public double[] Inputs { get; set; }
    public double[,] Outputs { get; set; }
}