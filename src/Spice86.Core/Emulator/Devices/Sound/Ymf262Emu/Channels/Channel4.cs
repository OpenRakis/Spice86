using System;
using Spice86.Core.Emulator.Devices.Sound.Ymf262Emu.Operators;

namespace Spice86.Core.Emulator.Devices.Sound.Ymf262Emu.Channels;

/// <summary>
/// Emulates a 4-operator OPL channel.
/// </summary>
internal sealed class Channel4 : Channel
{
    private readonly Operator op1, op2, op3, op4;

    /// <summary>
    /// Initializes a new instance of the Channel4 class.
    /// </summary>
    /// <param name="baseAddress">Base address of the channel's registers.</param>
    /// <param name="o1">First operator in the channel.</param>
    /// <param name="o2">Second operator in the channel.</param>
    /// <param name="o3">Third operator in the channel.</param>
    /// <param name="o4">Fourth operator in the channel.</param>
    /// <param name="opl">FmSynthesizer instance which owns the channel.</param>
    public Channel4(int baseAddress, Operator o1, Operator o2, Operator o3, Operator o4, FmSynthesizer opl)
        : base(baseAddress, opl)
    {
        this.op1 = o1;
        this.op2 = o2;
        this.op3 = o3;
        this.op4 = o4;
    }

    /// <summary>
    /// Returns an array containing the channel's output values.
    /// </summary>
    /// <returns>Array containing the channel's output values.</returns>
    public override void GetChannelOutput(Span<double> output)
    {
        double channelOutput = 0, op1Output = 0;
        int secondChannelBaseAddress = this.channelBaseAddress + 3;
        int secondCnt = this.opl.registers[secondChannelBaseAddress + CHD1_CHC1_CHB1_CHA1_FB3_CNT1_Offset] & 0x1;
        int cnt4op = (this.cnt << 1) | secondCnt;
        var feedbackOutput = (this.feedback0 + this.feedback1) / 2;
        double op2Output;
        double op3Output;
        double op4Output;

        switch (cnt4op)
        {
            case 0:
                if (this.op4.envelopeGenerator.State == AdsrState.Off)
                {
                    this.GetFourChannelOutput(0, output);
                    return;
                }

                op1Output = this.op1.GetOperatorOutput(feedbackOutput);
                op2Output = this.op2.GetOperatorOutput(op1Output * toPhase);
                op3Output = this.op3.GetOperatorOutput(op2Output * toPhase);
                channelOutput = this.op4.GetOperatorOutput(op3Output * toPhase);
                break;

            case 1:
                if (this.op2.envelopeGenerator.State == AdsrState.Off && this.op4.envelopeGenerator.State == AdsrState.Off)
                {
                    this.GetFourChannelOutput(0, output);
                    return;
                }

                op1Output = this.op1.GetOperatorOutput(feedbackOutput);
                op2Output = this.op2.GetOperatorOutput(op1Output * toPhase);
                op3Output = this.op3.GetOperatorOutput(Operator.NoModulator);
                op4Output = this.op4.GetOperatorOutput(op3Output * toPhase);

                channelOutput = (op2Output + op4Output) / 2;
                break;

            case 2:
                if (this.op1.envelopeGenerator.State == AdsrState.Off && this.op4.envelopeGenerator.State == AdsrState.Off)
                {
                    this.GetFourChannelOutput(0, output);
                    return;
                }

                op1Output = this.op1.GetOperatorOutput(feedbackOutput);
                op2Output = this.op2.GetOperatorOutput(Operator.NoModulator);
                op3Output = this.op3.GetOperatorOutput(op2Output * toPhase);
                op4Output = this.op4.GetOperatorOutput(op3Output * toPhase);

                channelOutput = (op1Output + op4Output) / 2;
                break;

            case 3:
                if (this.op1.envelopeGenerator.State == AdsrState.Off && this.op3.envelopeGenerator.State == AdsrState.Off && op4.envelopeGenerator.State == AdsrState.Off)
                {
                    this.GetFourChannelOutput(0, output);
                    return;
                }

                op1Output = this.op1.GetOperatorOutput(feedbackOutput);
                op2Output = this.op2.GetOperatorOutput(Operator.NoModulator);
                op3Output = this.op3.GetOperatorOutput(op2Output * toPhase);
                op4Output = this.op4.GetOperatorOutput(Operator.NoModulator);

                channelOutput = (op1Output + op3Output + op4Output) / 3;
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
        op1.KeyOn();
        op2.KeyOn();
        op3.KeyOn();
        op4.KeyOn();
        feedback0 = feedback1 = 0;
    }
    /// <summary>
    /// Disables channel output.
    /// </summary>
    public override void KeyOff()
    {
        this.op1.KeyOff();
        this.op2.KeyOff();
        this.op3.KeyOff();
        this.op4.KeyOff();
    }
    /// <summary>
    /// Updates the state of all of the operators in the channel.
    /// </summary>
    public override void UpdateOperators()
    {
        int keyScaleNumber = this.block * 2 + ((this.fnumh >> opl.nts) & 0x01);
        int f_number = (this.fnumh << 8) | fnuml;
        this.op1.UpdateOperator(keyScaleNumber, f_number, this.block);
        this.op2.UpdateOperator(keyScaleNumber, f_number, this.block);
        this.op3.UpdateOperator(keyScaleNumber, f_number, this.block);
        this.op4.UpdateOperator(keyScaleNumber, f_number, this.block);
    }
}
