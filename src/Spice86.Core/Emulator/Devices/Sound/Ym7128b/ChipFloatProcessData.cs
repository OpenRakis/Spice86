namespace Spice86.Core.Emulator.Devices.Sound.Ym7128b;
internal struct ChipFloatProcessData {
    public ChipFloatProcessData() {
        Inputs = new double[(int)InputChannel.Count];
        Outputs = new double[(int)OutputChannel.Count][];
        Outputs[0] = new double[(int)DatasheetSpecs.Oversampling];
        Outputs[1] = new double[(int)DatasheetSpecs.Oversampling];
    }

    public double[] Inputs { get; set; }
    public double[][] Outputs { get; set; }
}