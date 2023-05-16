namespace Spice86.Core.Emulator.Devices.Sound.Ymf262Emu.Operators;
using System;

/// <summary>
/// Emulates the snare drum OPL operator.
/// </summary>
internal sealed class SnareDrum : Operator {
    private readonly Random _random = new();

    /// <summary>
    /// Initializes a new instance of the SnareDrum operator.
    /// </summary>
    /// <param name="opl">FmSynthesizer instance which owns the operator.</param>
    public SnareDrum(FmSynthesizer opl)
        : base(0x14, opl) {
    }

    /// <summary>
    /// Returns the current output value of the operator.
    /// </summary>
    /// <param name="modulator">Modulation factor to apply to the output.</param>
    /// <returns>Current output value of the operator.</returns>
    public override double GetOperatorOutput(double modulator) {
        if (EnvelopeGenerator.State == AdsrState.Off) {
            return 0;
        }

        double envelopeInDb = EnvelopeGenerator.GetEnvelope(Egt, Am);
        Envelope = Math.Pow(10, envelopeInDb / 10.0);

        // If it is in OPL2 mode, use first four waveforms only:
        int waveIndex = Ws & ((Opl.IsOpl3Mode << 2) + 3);

        Phase = Opl.HighHatOperator.Phase * 2;
        double operatorOutput = GetOutput(modulator, Phase, waveIndex);
        double noise = _random.NextDouble() * Envelope;

        if (operatorOutput / Envelope is not 1 and not (-1))
        {
            if (operatorOutput > 0) {
                operatorOutput = noise;
            } else if (operatorOutput < 0) {
                operatorOutput = -noise;
            } else {
                operatorOutput = 0;
            }
        }

        return operatorOutput * 2;
    }
}
