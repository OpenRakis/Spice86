namespace Spice86.Core.Emulator.Devices.Sound.Ym7128b;
public struct ChipFixedProcessData {
    public ChipFixedProcessData() {
        Inputs = new short[(int)InputChannel.Count];
        Outputs = new short[(int)OutputChannel.Count][];
        Outputs[0] = new short[(int)DatasheetSpecs.Oversampling];
        Outputs[1] = new short[(int)DatasheetSpecs.Oversampling];
    }

    public short[] Inputs { get; set; }
    public short[][] Outputs { get; set; }
}