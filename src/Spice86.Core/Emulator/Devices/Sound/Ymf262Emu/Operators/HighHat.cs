using System;

namespace Spice86.Core.Emulator.Devices.Sound.Ymf262Emu.Operators;

/// <summary>
/// Emulates the highhat OPL operator.
/// </summary>
internal sealed class HighHat : TopCymbal
{
    private readonly Random random = new Random();

    /// <summary>
    /// Initializes a new instance of the HighHat class.
    /// </summary>
    /// <param name="opl">FmSynthesizer instance which owns the operator.</param>
    public HighHat(FmSynthesizer opl)
        : base(0x11, opl)
    {
    }

    /// <summary>
    /// Returns the current output value of the operator.
    /// </summary>
    /// <param name="modulator">Modulation factor to apply to the output.</param>
    /// <returns>Current output value of the operator.</returns>
    public override double GetOperatorOutput(double modulator)
    {
        var topCymbalOperatorPhase = this.opl.topCymbalOperator.phase * PhaseMultiplierTable[this.opl.topCymbalOperator.mult];
        var operatorOutput = this.GetOperatorOutput(modulator, topCymbalOperatorPhase);
        if (operatorOutput == 0)
            operatorOutput = this.random.NextDouble() * this.envelope;

        return operatorOutput;
    }
}
