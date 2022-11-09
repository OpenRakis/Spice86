namespace Spice86.Core.Emulator.Devices.Sound.Ymf262Emu.Channels;
using System;

using Spice86.Core.Emulator.Devices.Sound.Ymf262Emu.Operators;

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
        op1 = o1;
        op2 = o2;
        op3 = o3;
        op4 = o4;
    }

    /// <summary>
    /// Returns an array containing the channel's output values.
    /// </summary>
    /// <returns>Array containing the channel's output values.</returns>
    public override void GetChannelOutput(Span<double> output)
    {
        double channelOutput = 0, op1Output = 0;
        int secondChannelBaseAddress = channelBaseAddress + 3;
        int secondCnt = opl.registers[secondChannelBaseAddress + CHD1_CHC1_CHB1_CHA1_FB3_CNT1_Offset] & 0x1;
        int cnt4op = (cnt << 1) | secondCnt;
        double feedbackOutput = (feedback0 + feedback1) / 2;
        double op2Output;
        double op3Output;
        double op4Output;

        switch (cnt4op)
        {
            case 0:
                if (op4.envelopeGenerator.State == AdsrState.Off)
                {
                    GetFourChannelOutput(0, output);
                    return;
                }

                op1Output = op1.GetOperatorOutput(feedbackOutput);
                op2Output = op2.GetOperatorOutput(op1Output * toPhase);
                op3Output = op3.GetOperatorOutput(op2Output * toPhase);
                channelOutput = op4.GetOperatorOutput(op3Output * toPhase);
                break;

            case 1:
                if (op2.envelopeGenerator.State == AdsrState.Off && op4.envelopeGenerator.State == AdsrState.Off)
                {
                    GetFourChannelOutput(0, output);
                    return;
                }

                op1Output = op1.GetOperatorOutput(feedbackOutput);
                op2Output = op2.GetOperatorOutput(op1Output * toPhase);
                op3Output = op3.GetOperatorOutput(Operator.NoModulator);
                op4Output = op4.GetOperatorOutput(op3Output * toPhase);

                channelOutput = (op2Output + op4Output) / 2;
                break;

            case 2:
                if (op1.envelopeGenerator.State == AdsrState.Off && op4.envelopeGenerator.State == AdsrState.Off)
                {
                    GetFourChannelOutput(0, output);
                    return;
                }

                op1Output = op1.GetOperatorOutput(feedbackOutput);
                op2Output = op2.GetOperatorOutput(Operator.NoModulator);
                op3Output = op3.GetOperatorOutput(op2Output * toPhase);
                op4Output = op4.GetOperatorOutput(op3Output * toPhase);

                channelOutput = (op1Output + op4Output) / 2;
                break;

            case 3:
                if (op1.envelopeGenerator.State == AdsrState.Off && op3.envelopeGenerator.State == AdsrState.Off && op4.envelopeGenerator.State == AdsrState.Off)
                {
                    GetFourChannelOutput(0, output);
                    return;
                }

                op1Output = op1.GetOperatorOutput(feedbackOutput);
                op2Output = op2.GetOperatorOutput(Operator.NoModulator);
                op3Output = op3.GetOperatorOutput(op2Output * toPhase);
                op4Output = op4.GetOperatorOutput(Operator.NoModulator);

                channelOutput = (op1Output + op3Output + op4Output) / 3;
                break;
        }

        feedback0 = feedback1;
        feedback1 = (op1Output * feedbackTable[fb]) % 1;

        GetFourChannelOutput(channelOutput, output);
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
        op1.KeyOff();
        op2.KeyOff();
        op3.KeyOff();
        op4.KeyOff();
    }
    /// <summary>
    /// Updates the state of all of the operators in the channel.
    /// </summary>
    public override void UpdateOperators()
    {
        int keyScaleNumber = (block * 2) + ((fnumh >> opl.nts) & 0x01);
        int f_number = (fnumh << 8) | fnuml;
        op1.UpdateOperator(keyScaleNumber, f_number, block);
        op2.UpdateOperator(keyScaleNumber, f_number, block);
        op3.UpdateOperator(keyScaleNumber, f_number, block);
        op4.UpdateOperator(keyScaleNumber, f_number, block);
    }
}
