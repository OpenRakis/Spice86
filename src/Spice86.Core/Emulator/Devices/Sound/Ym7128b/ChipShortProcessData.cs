namespace Spice86.Core.Emulator.Devices.Sound.Ym7128b;
internal struct ChipShortProcessData {
    public ChipShortProcessData() {
        Inputs = new short[(int)InputChannel.Count];
        Outputs = new short[(int)OutputChannel.Count];
    }

    public short[] Inputs { get; set; }
    public short[] Outputs { get; set; }
}