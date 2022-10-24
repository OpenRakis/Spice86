using System;
using Spice86.Core.Emulator.Devices.Sound.Ymf262Emu.Operators;

namespace Spice86.Core.Emulator.Devices.Sound.Ymf262Emu.Channels;

/// <summary>
/// Emulates a 2-operator OPL channel.
/// </summary>
internal class Channel2 : Channel
{
    public readonly Operator op1, op2;

    /// <summary>
    /// Initializes a new instance of the Channel2 class.
    /// </summary>
    /// <param name="baseAddress">Base address of the channel's registers.</param>
    /// <param name="o1">First operator in the channel.</param>
    /// <param name="o2">Second operator in the channel.</param>
    /// <param name="opl">FmSynthesizer instance which owns the channel.</param>
    public Channel2(int baseAddress, Operator o1, Operator o2, FmSynthesizer opl)
        : base(baseAddress, opl)
    {
        this.op1 = o1;
        this.op2 = o2;
    }

    /// <summary>
    /// Returns an array containing the channel's output values.
    /// </summary>
    /// <returns>Array containing the channel's output values.</returns>
    public override void GetChannelOutput(Span<double> output)
    {
        double channelOutput = 0, op1Output = 0;
        double feedbackOutput = (this.feedback0 + this.feedback1) / 2;

        switch (cnt)
        {
            // CNT = 0, the operators are in series, with the first in feedback.
            case 0:
                if (this.op2.envelopeGenerator.State == AdsrState.Off)
                {
                    this.GetFourChannelOutput(0, output);
                    return;
                }

                op1Output = this.op1.GetOperatorOutput(feedbackOutput);
                channelOutput = this.op2.GetOperatorOutput(op1Output * toPhase);
                break;

            // CNT = 1, the operators are in parallel, with the first in feedback.    
            case 1:
                if (this.op1.envelopeGenerator.State == AdsrState.Off && this.op2.envelopeGenerator.State == AdsrState.Off)
                {
                    this.GetFourChannelOutput(0, output);
                    return;
                }

                op1Output = this.op1.GetOperatorOutput(feedbackOutput);
                double op2Output = this.op2.GetOperatorOutput(Operator.NoModulator);
                channelOutput = (op1Output + op2Output) / 2;
                break;
        }

        this.feedback0 = this.feedback1;
        this.feedback1 = (op1Output * feedbackTable[this.fb]) % 1;
        this.GetFourChannelOutput(channelOutput, output);
    }
    /// <summary>
    /// Activates channel output.
    /// </summary>
    public override void KeyOn()
    {
        this.op1.KeyOn();
        this.op2.KeyOn();
        this.feedback0 = 0;
        this.feedback1 = 0;
    }
    /// <summary>
    /// Disables channel output.
    /// </summary>
    public override void KeyOff()
    {
        this.op1.KeyOff();
        this.op2.KeyOff();
    }
    /// <summary>
    /// Updates the state of all of the operators in the channel.
    /// </summary>
    public override void UpdateOperators()
    {
        int keyScaleNumber = (this.block * 2) + ((this.fnumh >> this.opl.nts) & 0x01);
        int f_number = (this.fnumh << 8) | this.fnuml;
        this.op1.UpdateOperator(keyScaleNumber, f_number, block);
        this.op2.UpdateOperator(keyScaleNumber, f_number, block);
    }
}
