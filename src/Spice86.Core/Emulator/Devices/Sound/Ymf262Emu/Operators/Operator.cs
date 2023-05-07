namespace Spice86.Core.Emulator.Devices.Sound.Ymf262Emu.Operators;
using System;

/// <summary>
/// Emulates a single OPL operator.
/// </summary>
internal class Operator {
    public readonly AdsrCalculator EnvelopeGenerator;
    public double Phase;
    public int Mult, Ar;

    protected double Envelope;
    protected readonly FmSynthesizer Opl;
    protected int Am, Egt, Ws;

    protected static readonly double[] PhaseMultiplierTable = { 0.5, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 10, 12, 12, 15, 15 };

    private readonly int _operatorBaseAddress;
    private int _ksr, _ksl, _tl, _dr, _sl, _rr, _vib;
    private int _keyScaleNumber, _fNumber, _block;
    private double _phaseIncrement;

    public const double NoModulator = 0;

    private const int Wavelength = 1024;
    private const int Am1Vib1Egt1Ksr1Mult4Offset = 0x20;
    private const int Ksl2Tl6Offset = 0x40;
    private const int Ar4Dr4Offset = 0x60;
    private const int Sl4Rr4Offset = 0x80;
    private const int _5_WS3_Offset = 0xE0;

    /// <summary>
    /// Initializes a new instance of the Operator class.
    /// </summary>
    /// <param name="baseAddress">Base operator register address.</param>
    /// <param name="opl">FmSynthesizer instance which owns the operator.</param>
    public Operator(int baseAddress, FmSynthesizer opl) {
        _operatorBaseAddress = baseAddress;
        this.Opl = opl;
        EnvelopeGenerator = new AdsrCalculator(opl);
    }

    public void Update_AM1_VIB1_EGT1_KSR1_MULT4()
    {
        int am1Vib1Egt1Ksr1Mult4 = Opl.Registers[_operatorBaseAddress + Am1Vib1Egt1Ksr1Mult4Offset];

        // Amplitude Modulation. This register is used int EnvelopeGenerator.getEnvelope();
        Am = (am1Vib1Egt1Ksr1Mult4 & 0x80) >> 7;
        // Vibrato. This register is used in PhaseGenerator.getPhase();
        _vib = (am1Vib1Egt1Ksr1Mult4 & 0x40) >> 6;
        // Envelope Generator Type. This register is used in EnvelopeGenerator.getEnvelope();
        Egt = (am1Vib1Egt1Ksr1Mult4 & 0x20) >> 5;
        // Key Scale Rate. Sets the actual envelope rate together with rate and keyScaleNumber.
        // This register os used in EnvelopeGenerator.setActualAttackRate().
        _ksr = (am1Vib1Egt1Ksr1Mult4 & 0x10) >> 4;
        // Multiple. Multiplies the Channel.baseFrequency to get the Operator.operatorFrequency.
        // This register is used in PhaseGenerator.setFrequency().
        Mult = am1Vib1Egt1Ksr1Mult4 & 0x0F;

        UpdateFrequency();
        EnvelopeGenerator.SetActualAttackRate(Ar, _ksr, _keyScaleNumber);
        EnvelopeGenerator.SetActualDecayRate(_dr, _ksr, _keyScaleNumber);
        EnvelopeGenerator.SetActualReleaseRate(_rr, _ksr, _keyScaleNumber);
    }
    
    public void Update_KSL2_TL6()
    {
        int ksl2Tl6 = Opl.Registers[_operatorBaseAddress + Ksl2Tl6Offset];

        // Key Scale Level. Sets the attenuation in accordance with the octave.
        _ksl = (ksl2Tl6 & 0xC0) >> 6;
        // Total Level. Sets the overall damping for the envelope.
        _tl = ksl2Tl6 & 0x3F;

        EnvelopeGenerator.SetAtennuation(_fNumber, _block, _ksl);
        EnvelopeGenerator.TotalLevel = _tl;
    }
    
    public void Update_AR4_DR4()
    {
        int ar4Dr4 = Opl.Registers[_operatorBaseAddress + Ar4Dr4Offset];

        // Attack Rate.
        Ar = (ar4Dr4 & 0xF0) >> 4;
        // Decay Rate.
        _dr = ar4Dr4 & 0x0F;

        EnvelopeGenerator.SetActualAttackRate(Ar, _ksr, _keyScaleNumber);
        EnvelopeGenerator.SetActualDecayRate(_dr, _ksr, _keyScaleNumber);
    }
    
    public void Update_SL4_RR4()
    {
        int sl4Rr4 = Opl.Registers[_operatorBaseAddress + Sl4Rr4Offset];

        // Sustain Level.
        _sl = (sl4Rr4 & 0xF0) >> 4;
        // Release Rate.
        _rr = sl4Rr4 & 0x0F;

        EnvelopeGenerator.SustainLevel = _sl;
        EnvelopeGenerator.SetActualReleaseRate(_rr, _ksr, _keyScaleNumber);
    }
    
    public void Update_5_WS3()
    {
        int _5_ws3 = Opl.Registers[_operatorBaseAddress + _5_WS3_Offset];
        Ws = _5_ws3 & 0x07;
    }
    
    /// <summary>
    /// Returns the current output value of the operator.
    /// </summary>
    /// <param name="modulator">Modulation factor to apply to the output.</param>
    /// <returns>Current output value of the operator.</returns>
    public virtual double GetOperatorOutput(double modulator)
    {
        if (EnvelopeGenerator.State == AdsrState.Off) {
            return 0;
        }

        double envelopeInDb = EnvelopeGenerator.GetEnvelope(Egt, Am);
        Envelope = Math.Pow(10, envelopeInDb / 10.0);

        // If it is in OPL2 mode, use first four waveforms only:
        Ws &= (Opl.IsOpl3Mode << 2) + 3;

        UpdatePhase();

        double operatorOutput = GetOutput(modulator, Phase, Ws);
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
        return GetWaveformValue(waveform, sampleIndex) * Envelope;
    }
    
    public virtual void KeyOn()
    {
        if (Ar > 0)
        {
            EnvelopeGenerator.KeyOn();
            Phase = 0;
        }
        else
        {
            EnvelopeGenerator.State = AdsrState.Off;
        }
    }
    
    public virtual void KeyOff()
    {
        EnvelopeGenerator.KeyOff();
    }
    
    public virtual void UpdateOperator(int ksn, int fNum, int blk)
    {
        _keyScaleNumber = ksn;
        _fNumber = fNum;
        _block = blk;
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
        if (_vib == 1) {
            Phase += _phaseIncrement * VibratoGenerator.GetValue(Opl.Dvb, Opl.VibratoIndex);
        } else {
            Phase += _phaseIncrement;
        }

        Phase %= 1;
    }

    /// <summary>
    /// Calculates and stores the current frequency of the operator.
    /// </summary>
    private void UpdateFrequency()
    {
        double baseFrequency = _fNumber * Math.Pow(2, _block - 1) * Opl.SampleRate / Math.Pow(2, 19);
        double operatorFrequency = baseFrequency * PhaseMultiplierTable[Mult];

        _phaseIncrement = operatorFrequency / Opl.SampleRate;
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

            default:
                if (i < 512) {
                    return Math.Pow(2, -(i * xFactor));
                } else {
                    return -Math.Pow(2, -(((i - 512) * xFactor) + (1 / 16d)));
                }
        }
    }
}
