using System;

namespace Spice86.Core.Emulator.Devices.Sound.Ymf262Emu.Operators;

/// <summary>
/// Emulates the top cymbal OPL operator.
/// </summary>
internal class TopCymbal : Operator
{
    /// <summary>
    /// Initializes a new instance of the TopCymbal class.
    /// </summary>
    /// <param name="baseAddress">Base operator register address.</param>
    /// <param name="opl">FmSynthesizer instance which owns the operator.</param>
    protected TopCymbal(int baseAddress, FmSynthesizer opl)
        : base(baseAddress, opl)
    {
    }
    /// <summary>
    /// Initializes a new instance of the TopCymbal class.
    /// </summary>
    /// <param name="opl">FmSynthesizer instance which owns the operator.</param>
    public TopCymbal(FmSynthesizer opl)
        : base(0x15, opl)
    {
    }

    /// <summary>
    /// Returns the current output value of the operator.
    /// </summary>
    /// <param name="modulator">Modulation factor to apply to the output.</param>
    /// <returns>Current output value of the operator.</returns>
    public override double GetOperatorOutput(double modulator)
    {
        double highHatOperatorPhase = opl.highHatOperator.phase * PhaseMultiplierTable[opl.highHatOperator.mult];
        // The Top Cymbal operator uses his own phase together with the High Hat phase.
        return GetOperatorOutput(modulator, highHatOperatorPhase);
    }
    public double GetOperatorOutput(double modulator, double externalPhase)
    {
        var envelopeInDB = this.envelopeGenerator.GetEnvelope(this.egt, this.am);
        this.envelope = Math.Pow(10, envelopeInDB / 10.0);

        this.UpdatePhase();

        int waveIndex = this.ws & ((this.opl.IsOpl3Mode << 2) + 3);

        // Empirically tested multiplied phase for the Top Cymbal:
        var carrierPhase = 8 * this.phase % 1;
        var modulatorPhase = externalPhase;
        var modulatorOutput = this.GetOutput(NoModulator, modulatorPhase, waveIndex);
        var carrierOutput = this.GetOutput(modulatorOutput, carrierPhase, waveIndex);

        int cycles = 4;
        if ((carrierPhase * cycles) % cycles > 0.1)
            carrierOutput = 0;

        return carrierOutput * 2;
    }
}
