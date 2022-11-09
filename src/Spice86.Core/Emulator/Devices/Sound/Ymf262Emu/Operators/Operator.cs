namespace Spice86.Core.Emulator.Devices.Sound.Ymf262Emu.Operators;
using System;

/// <summary>
/// Emulates a single OPL operator.
/// </summary>
internal class Operator
{
    public readonly AdsrCalculator envelopeGenerator;
    public double phase;
    public int mult, ar;

    protected double envelope;
    protected readonly FmSynthesizer opl;
    protected int am, egt, ws;

    protected static readonly double[] PhaseMultiplierTable = { 0.5, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 10, 12, 12, 15, 15 };

    private readonly int operatorBaseAddress;
    private int ksr, ksl, tl, dr, sl, rr, vib;
    private int keyScaleNumber, f_number, block;
    private double phaseIncrement;

    public const double NoModulator = 0;

    private const int Wavelength = 1024;
    private const int AM1_VIB1_EGT1_KSR1_MULT4_Offset = 0x20;
    private const int KSL2_TL6_Offset = 0x40;
    private const int AR4_DR4_Offset = 0x60;
    private const int SL4_RR4_Offset = 0x80;
    private const int _5_WS3_Offset = 0xE0;

    /// <summary>
    /// Initializes a new instance of the Operator class.
    /// </summary>
    /// <param name="baseAddress">Base operator register address.</param>
    /// <param name="opl">FmSynthesizer instance which owns the operator.</param>
    public Operator(int baseAddress, FmSynthesizer opl)
    {
        operatorBaseAddress = baseAddress;
        this.opl = opl;
        envelopeGenerator = new AdsrCalculator(opl);
    }

    public void Update_AM1_VIB1_EGT1_KSR1_MULT4()
    {
        int am1_vib1_egt1_ksr1_mult4 = opl.registers[operatorBaseAddress + AM1_VIB1_EGT1_KSR1_MULT4_Offset];

        // Amplitude Modulation. This register is used int EnvelopeGenerator.getEnvelope();
        am = (am1_vib1_egt1_ksr1_mult4 & 0x80) >> 7;
        // Vibrato. This register is used in PhaseGenerator.getPhase();
        vib = (am1_vib1_egt1_ksr1_mult4 & 0x40) >> 6;
        // Envelope Generator Type. This register is used in EnvelopeGenerator.getEnvelope();
        egt = (am1_vib1_egt1_ksr1_mult4 & 0x20) >> 5;
        // Key Scale Rate. Sets the actual envelope rate together with rate and keyScaleNumber.
        // This register os used in EnvelopeGenerator.setActualAttackRate().
        ksr = (am1_vib1_egt1_ksr1_mult4 & 0x10) >> 4;
        // Multiple. Multiplies the Channel.baseFrequency to get the Operator.operatorFrequency.
        // This register is used in PhaseGenerator.setFrequency().
        mult = am1_vib1_egt1_ksr1_mult4 & 0x0F;

        UpdateFrequency();
        envelopeGenerator.SetActualAttackRate(ar, ksr, keyScaleNumber);
        envelopeGenerator.SetActualDecayRate(dr, ksr, keyScaleNumber);
        envelopeGenerator.SetActualReleaseRate(rr, ksr, keyScaleNumber);
    }
    public void Update_KSL2_TL6()
    {
        int ksl2_tl6 = opl.registers[operatorBaseAddress + KSL2_TL6_Offset];

        // Key Scale Level. Sets the attenuation in accordance with the octave.
        ksl = (ksl2_tl6 & 0xC0) >> 6;
        // Total Level. Sets the overall damping for the envelope.
        tl = ksl2_tl6 & 0x3F;

        envelopeGenerator.SetAtennuation(f_number, block, ksl);
        envelopeGenerator.TotalLevel = tl;
    }
    public void Update_AR4_DR4()
    {
        int ar4_dr4 = opl.registers[operatorBaseAddress + AR4_DR4_Offset];

        // Attack Rate.
        ar = (ar4_dr4 & 0xF0) >> 4;
        // Decay Rate.
        dr = ar4_dr4 & 0x0F;

        envelopeGenerator.SetActualAttackRate(ar, ksr, keyScaleNumber);
        envelopeGenerator.SetActualDecayRate(dr, ksr, keyScaleNumber);
    }
    public void Update_SL4_RR4()
    {
        int sl4_rr4 = opl.registers[operatorBaseAddress + SL4_RR4_Offset];

        // Sustain Level.
        sl = (sl4_rr4 & 0xF0) >> 4;
        // Release Rate.
        rr = sl4_rr4 & 0x0F;

        envelopeGenerator.SustainLevel = sl;
        envelopeGenerator.SetActualReleaseRate(rr, ksr, keyScaleNumber);
    }
    public void Update_5_WS3()
    {
        int _5_ws3 = opl.registers[operatorBaseAddress + _5_WS3_Offset];
        ws = _5_ws3 & 0x07;
    }
    /// <summary>
    /// Returns the current output value of the operator.
    /// </summary>
    /// <param name="modulator">Modulation factor to apply to the output.</param>
    /// <returns>Current output value of the operator.</returns>
    public virtual double GetOperatorOutput(double modulator)
    {
        if (envelopeGenerator.State == AdsrState.Off) {
            return 0;
        }

        double envelopeInDB = envelopeGenerator.GetEnvelope(egt, am);
        envelope = Math.Pow(10, envelopeInDB / 10.0);

        // If it is in OPL2 mode, use first four waveforms only:
        ws &= (opl.IsOpl3Mode << 2) + 3;

        UpdatePhase();

        double operatorOutput = GetOutput(modulator, phase, ws);
        return operatorOutput;
    }
    public virtual double GetOutput(double modulator, double outputPhase, int waveform)
    {
        outputPhase = (outputPhase + modulator) % 1;
        if (outputPhase < 0)
        {
            outputPhase++;
            // If the double could not afford to be less than 1:
            outputPhase %= 1;
        }

        int sampleIndex = (int)(outputPhase * Wavelength);
        return GetWaveformValue(waveform, sampleIndex) * envelope;
    }
    public virtual void KeyOn()
    {
        if (ar > 0)
        {
            envelopeGenerator.KeyOn();
            phase = 0;
        }
        else
        {
            envelopeGenerator.State = AdsrState.Off;
        }
    }
    public virtual void KeyOff()
    {
        envelopeGenerator.KeyOff();
    }
    public virtual void UpdateOperator(int ksn, int f_num, int blk)
    {
        keyScaleNumber = ksn;
        f_number = f_num;
        block = blk;
        Update_AM1_VIB1_EGT1_KSR1_MULT4();
        Update_KSL2_TL6();
        Update_AR4_DR4();
        Update_SL4_RR4();
        Update_5_WS3();
    }

    /// <summary>
    /// Calculates and stores the current phase of the operator.
    /// </summary>
    protected void UpdatePhase()
    {
        if (vib == 1) {
            phase += phaseIncrement * VibratoGenerator.GetValue(opl.dvb, opl.vibratoIndex);
        } else {
            phase += phaseIncrement;
        }

        phase %= 1;
    }

    /// <summary>
    /// Calculates and stores the current frequency of the operator.
    /// </summary>
    private void UpdateFrequency()
    {
        double baseFrequency = f_number * Math.Pow(2, block - 1) * opl.SampleRate / Math.Pow(2, 19);
        double operatorFrequency = baseFrequency * PhaseMultiplierTable[mult];

        phaseIncrement = operatorFrequency / opl.SampleRate;
    }

    private static double GetWaveformValue(int w, int i)
    {
        const double thetaIncrement = 2 * Math.PI / 1024;
        const double xFactor = 1 * 16d / 256d;

        switch (w)
        {
            case 0:
                return Math.Sin(i * thetaIncrement);

            case 1:
                return i < 512 ? Math.Sin(i * thetaIncrement) : 0;

            case 2:
                return Math.Sin((i & 511) * thetaIncrement);

            case 3:
                if (i < 256) {
                    return Math.Sin(i * thetaIncrement);
                } else if (i is >= 512 and < 768) {
                    return Math.Sin((i - 512) * thetaIncrement);
                } else {
                    return 0;
                }

            case 4:
                return i < 512 ? Math.Sin(i * 2 * thetaIncrement) : 0;

            case 5:
                return i < 512 ? Math.Sin(((i * 2) & 511) * thetaIncrement) : 0;

            case 6:
                return i < 512 ? 1 : -1;

            case 7:
            default:
                if (i < 512) {
                    return Math.Pow(2, -(i * xFactor));
                } else {
                    return -Math.Pow(2, -(((i - 512) * xFactor) + (1 / 16d)));
                }
        }
    }
}
