namespace Spice86.Core.Emulator.Devices.Sound.Ymf262Emu.Channels;
using System;

using Spice86.Core.Emulator.Devices.Sound.Ymf262Emu.Operators;

/// <summary>
/// Emulates a 4-operator OPL channel.
/// </summary>
internal sealed class Channel4 : Channel {
    private readonly Operator? _op1;
    private readonly Operator? _op2;
    private readonly Operator? _op3;
    private readonly Operator? _op4;

    /// <summary>
    /// Initializes a new instance of the Channel4 class.
    /// </summary>
    /// <param name="baseAddress">Base address of the channel's registers.</param>
    /// <param name="o1">First operator in the channel.</param>
    /// <param name="o2">Second operator in the channel.</param>
    /// <param name="o3">Third operator in the channel.</param>
    /// <param name="o4">Fourth operator in the channel.</param>
    /// <param name="opl">FmSynthesizer instance which owns the channel.</param>
    public Channel4(int baseAddress, Operator? o1, Operator? o2, Operator? o3, Operator? o4, FmSynthesizer opl)
        : base(baseAddress, opl) {
        _op1 = o1;
        _op2 = o2;
        _op3 = o3;
        _op4 = o4;
    }

    /// <summary>
    /// Returns an array containing the channel's output values.
    /// </summary>
    /// <returns>Array containing the channel's output values.</returns>
    public override void GetChannelOutput(Span<double> output) {
        double channelOutput = 0, op1Output = 0;
        int secondChannelBaseAddress = ChannelBaseAddress + 3;
        int secondCnt = Opl.Registers[secondChannelBaseAddress + Chd1Chc1Chb1Cha1Fb3Cnt1Offset] & 0x1;
        int cnt4Op = (Cnt << 1) | secondCnt;
        double feedbackOutput = (Feedback0 + Feedback1) / 2;
        double op2Output = 0;
        double op3Output = 0;
        double op4Output = 0;

        switch (cnt4Op) {
            case 0:
                if (_op4 is {EnvelopeGenerator.State: AdsrState.Off}) {
                    GetFourChannelOutput(0, output);
                    return;
                }

                if (_op1 != null) {
                    op1Output = _op1.GetOperatorOutput(feedbackOutput);
                }

                if (_op2 != null) {
                    op2Output = _op2.GetOperatorOutput(op1Output * ToPhase);
                }

                if (_op3 != null) {
                    op3Output = _op3.GetOperatorOutput(op2Output * ToPhase);
                }

                if (_op4 != null) {
                    channelOutput = _op4.GetOperatorOutput(op3Output * ToPhase);
                }

                break;

            case 1:
                if (_op4 != null && _op2 is {EnvelopeGenerator.State: AdsrState.Off} && _op4.EnvelopeGenerator.State == AdsrState.Off) {
                    GetFourChannelOutput(0, output);
                    return;
                }

                if (_op1 != null) {
                    op1Output = _op1.GetOperatorOutput(feedbackOutput);
                }

                if (_op2 != null) {
                    op2Output = _op2.GetOperatorOutput(op1Output * ToPhase);
                }

                if (_op3 != null) {
                    op3Output = _op3.GetOperatorOutput(Operator.NoModulator);
                }

                if (_op4 != null) {
                    op4Output = _op4.GetOperatorOutput(op3Output * ToPhase);
                }

                channelOutput = (op2Output + op4Output) / 2;
                break;

            case 2:
                if (_op1 != null && _op4 != null && _op1.EnvelopeGenerator.State == AdsrState.Off && _op4.EnvelopeGenerator.State == AdsrState.Off) {
                    GetFourChannelOutput(0, output);
                    return;
                }

                if (_op1 != null) {
                    op1Output = _op1.GetOperatorOutput(feedbackOutput);
                }

                if (_op2 != null) {
                    op2Output = _op2.GetOperatorOutput(Operator.NoModulator);
                }

                if (_op3 != null) {
                    op3Output = _op3.GetOperatorOutput(op2Output * ToPhase);
                }

                if (_op4 != null) {
                    op4Output = _op4.GetOperatorOutput(op3Output * ToPhase);
                }

                channelOutput = (op1Output + op4Output) / 2;
                break;

            case 3:
                if (_op3 != null && _op4 != null && _op1 is {EnvelopeGenerator.State: AdsrState.Off} && _op3.EnvelopeGenerator.State == AdsrState.Off && _op4.EnvelopeGenerator.State == AdsrState.Off) {
                    GetFourChannelOutput(0, output);
                    return;
                }

                if (_op1 != null) {
                    op1Output = _op1.GetOperatorOutput(feedbackOutput);
                }

                if (_op2 != null) {
                    op2Output = _op2.GetOperatorOutput(Operator.NoModulator);
                }

                if (_op3 != null) {
                    op3Output = _op3.GetOperatorOutput(op2Output * ToPhase);
                }

                if (_op4 != null) {
                    op4Output = _op4.GetOperatorOutput(Operator.NoModulator);
                }

                channelOutput = (op1Output + op3Output + op4Output) / 3;
                break;
        }

        Feedback0 = Feedback1;
        Feedback1 = (op1Output * FeedbackTable[Fb]) % 1;

        GetFourChannelOutput(channelOutput, output);
    }

    /// <summary>
    /// Activates channel output.
    /// </summary>
    public override void KeyOn() {
        _op1?.KeyOn();
        _op2?.KeyOn();
        _op3?.KeyOn();
        _op4?.KeyOn();
        Feedback0 = Feedback1 = 0;
    }

    /// <summary>
    /// Disables channel output.
    /// </summary>
    public override void KeyOff() {
        _op1?.KeyOff();
        _op2?.KeyOff();
        _op3?.KeyOff();
        _op4?.KeyOff();
    }

    /// <summary>
    /// Updates the state of all of the operators in the channel.
    /// </summary>
    public override void UpdateOperators() {
        int keyScaleNumber = (Block * 2) + ((Fnumh >> Opl.Nts) & 0x01);
        int fNumber = (Fnumh << 8) | Fnuml;
        _op1?.UpdateOperator(keyScaleNumber, fNumber, Block);
        _op2?.UpdateOperator(keyScaleNumber, fNumber, Block);
        _op3?.UpdateOperator(keyScaleNumber, fNumber, Block);
        _op4?.UpdateOperator(keyScaleNumber, fNumber, Block);
    }
}
