namespace Spice86.Core.Emulator.Devices.Sound.Ymf262Emu.Channels;

using Spice86.Core.Emulator.Devices.Sound.Ymf262Emu.Operators;

internal sealed class BassDrum : Channel2 {
    public BassDrum(FmSynthesizer opl)
        : base(6, new Operator(0x10, opl), new Operator(0x13, opl), opl) {
    }

    public override void GetChannelOutput(Span<double> output) {
        // Bass Drum ignores first operator, when it is in series.
        if (Cnt == 1) {
            if (Op1 != null) {
                Op1.Ar = 0;
            }
        }

        base.GetChannelOutput(output);
    }

    // Key ON and OFF are unused in rhythm channels.
    public override void KeyOn() {
    }

    public override void KeyOff() {
    }
}
