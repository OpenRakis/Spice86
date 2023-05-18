namespace Spice86.Core.Emulator.Devices.Sound.Ymf262Emu.Channels;
using System;

using Spice86.Core.Emulator.Devices.Sound.Ymf262Emu.Operators;

/// <summary>
/// Emulates a 2-operator OPL channel.
/// </summary>
internal class Channel2 : Channel
{
    public readonly Operator? Op1;
    public readonly Operator? Op2;

    /// <summary>
    /// Initializes a new instance of the Channel2 class.
    /// </summary>
    /// <param name="baseAddress">Base address of the channel's registers.</param>
    /// <param name="o1">First operator in the channel.</param>
    /// <param name="o2">Second operator in the channel.</param>
    /// <param name="opl">FmSynthesizer instance which owns the channel.</param>
    public Channel2(int baseAddress, Operator? o1, Operator? o2, FmSynthesizer opl)
        : base(baseAddress, opl)
    {
        Op1 = o1;
        Op2 = o2;
    }

    /// <summary>
    /// Returns an array containing the channel's output values.
    /// </summary>
    /// <returns>Array containing the channel's output values.</returns>
    public override void GetChannelOutput(Span<double> output)
    {
        double channelOutput = 0, op1Output = 0;
        double feedbackOutput = (Feedback0 + Feedback1) / 2;

        switch (Cnt)
        {
            // CNT = 0, the operators are in series, with the first in feedback.
            case 0:
                if (Op2 != null && Op2.EnvelopeGenerator.State == AdsrState.Off)
                {
                    GetFourChannelOutput(0, output);
                    return;
                }

                if (Op1 != null) {
                    op1Output = Op1.GetOperatorOutput(feedbackOutput);
                }

                if (Op2 != null) {
                    channelOutput = Op2.GetOperatorOutput(op1Output * ToPhase);
                }

                break;

            // CNT = 1, the operators are in parallel, with the first in feedback.    
            case 1:
                if (Op2 != null && Op1 is {EnvelopeGenerator.State: AdsrState.Off} && Op2.EnvelopeGenerator.State == AdsrState.Off)
                {
                    GetFourChannelOutput(0, output);
                    return;
                }

                if (Op1 != null) {
                    op1Output = Op1.GetOperatorOutput(feedbackOutput);
                }

                if (Op2 != null) {
                    double op2Output = Op2.GetOperatorOutput(Operator.NoModulator);
                    channelOutput = (op1Output + op2Output) / 2;
                }

                break;
        }

        Feedback0 = Feedback1;
        Feedback1 = (op1Output * FeedbackTable[Fb]) % 1;
        GetFourChannelOutput(channelOutput, output);
    }
    
    /// <summary>
    /// Activates channel output.
    /// </summary>
    public override void KeyOn()
    {
        Op1?.KeyOn();
        Op2?.KeyOn();
        Feedback0 = 0;
        Feedback1 = 0;
    }
    
    /// <summary>
    /// Disables channel output.
    /// </summary>
    public override void KeyOff()
    {
        Op1?.KeyOff();
        Op2?.KeyOff();
    }
    
    /// <summary>
    /// Updates the state of all of the operators in the channel.
    /// </summary>
    public override void UpdateOperators()
    {
        int keyScaleNumber = (Block * 2) + ((Fnumh >> Opl.Nts) & 0x01);
        int fNumber = (Fnumh << 8) | Fnuml;
        Op1?.UpdateOperator(keyScaleNumber, fNumber, Block);
        Op2?.UpdateOperator(keyScaleNumber, fNumber, Block);
    }
}
