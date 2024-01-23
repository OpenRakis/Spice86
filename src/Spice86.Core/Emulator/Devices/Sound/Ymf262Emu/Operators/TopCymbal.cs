namespace Spice86.Core.Emulator.Devices.Sound.Ymf262Emu.Operators;
using System;

/// <summary>
/// Emulates the top cymbal OPL operator.
/// </summary>
internal class TopCymbal : Operator {
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
    public override double GetOperatorOutput(double modulator) {
        if (Opl.HighHatOperator == null) {
            return 0d;
        }
        double highHatOperatorPhase = Opl.HighHatOperator.Phase * PhaseMultiplierTable[Opl.HighHatOperator.Mult];
        // The Top Cymbal operator uses his own phase together with the High Hat phase.
        return GetOperatorOutput(modulator, highHatOperatorPhase);
    }

    public double GetOperatorOutput(double modulator, double externalPhase)
    {
        double envelopeInDb = EnvelopeGenerator.GetEnvelope(Egt, Am);
        Envelope = Math.Pow(10, envelopeInDb / 10.0);

        UpdatePhase();

        int waveIndex = Ws & ((Opl.IsOpl3Mode << 2) + 3);

        // Empirically tested multiplied phase for the Top Cymbal:
        double carrierPhase = 8 * Phase % 1;
        double modulatorPhase = externalPhase;
        double modulatorOutput = GetOutput(NoModulator, modulatorPhase, waveIndex);
        double carrierOutput = GetOutput(modulatorOutput, carrierPhase, waveIndex);

        const int cycles = 4;
        if ((carrierPhase * cycles) % cycles > 0.1) {
            carrierOutput = 0;
        }

        return carrierOutput * 2;
    }
}
