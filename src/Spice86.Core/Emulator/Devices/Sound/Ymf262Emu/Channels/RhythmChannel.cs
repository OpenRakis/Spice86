namespace Spice86.Core.Emulator.Devices.Sound.Ymf262Emu.Channels;
using System;

using Spice86.Core.Emulator.Devices.Sound.Ymf262Emu.Operators;

/// <summary>
/// Emulates a 2-operator rhythm OPL channel.
/// </summary>
internal sealed class RhythmChannel : Channel2 {
    /// <summary>
    /// Initializes a new instance of the RhythmChannel class.
    /// </summary>
    /// <param name="baseAddress">Base address of the channel's registers.</param>
    /// <param name="o1">First operator in the channel.</param>
    /// <param name="o2">Second operator in the channel.</param>
    /// <param name="opl">FmSynthesizer instance which owns the channel.</param>
    public RhythmChannel(int baseAddress, Operator? o1, Operator? o2, FmSynthesizer opl)
        : base(baseAddress, o1, o2, opl) {
    }

    /// <summary>
    /// Returns an array containing the channel's output values.
    /// </summary>
    /// <returns>Array containing the channel's output values.</returns>
    public override void GetChannelOutput(Span<double> output) {
        if (Op1 == null) {
            return;
        }

        double op1Output = Op1.GetOperatorOutput(Operator.NoModulator);
        if (Op2 == null) {
            return;
        }

        double op2Output = Op2.GetOperatorOutput(Operator.NoModulator);
        double channelOutput = (op1Output + op2Output) / 2;

        GetFourChannelOutput(channelOutput, output);
    }

    /// <summary>
    /// Activates channel output.
    /// </summary>
    public override void KeyOn() {
    }

    /// <summary>
    /// Disables channel output.
    /// </summary>
    public override void KeyOff() {
    }
}
