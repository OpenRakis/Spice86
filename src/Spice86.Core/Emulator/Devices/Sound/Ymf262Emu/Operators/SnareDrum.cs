namespace Spice86.Core.Emulator.Devices.Sound.Ymf262Emu.Operators;
using System;

/// <summary>
/// Emulates the snare drum OPL operator.
/// </summary>
internal sealed class SnareDrum : Operator
{
    private readonly Random random = new();

    /// <summary>
    /// Initializes a new instance of the SnareDrum operator.
    /// </summary>
    /// <param name="opl">FmSynthesizer instance which owns the operator.</param>
    public SnareDrum(FmSynthesizer opl)
        : base(0x14, opl)
    {
    }

    /// <summary>
    /// Returns the current output value of the operator.
    /// </summary>
    /// <param name="modulator">Modulation factor to apply to the output.</param>
    /// <returns>Current output value of the operator.</returns>
    public override double GetOperatorOutput(double modulator)
    {
        if (envelopeGenerator.State == AdsrState.Off) {
            return 0;
        }

        double envelopeInDB = envelopeGenerator.GetEnvelope(egt, am);
        envelope = Math.Pow(10, envelopeInDB / 10.0);

        // If it is in OPL2 mode, use first four waveforms only:
        int waveIndex = ws & ((opl.IsOpl3Mode << 2) + 3);

        phase = opl.highHatOperator.phase * 2;
        double operatorOutput = GetOutput(modulator, phase, waveIndex);
        double noise = random.NextDouble() * envelope;

        if (operatorOutput / envelope is not 1 and not (-1))
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
