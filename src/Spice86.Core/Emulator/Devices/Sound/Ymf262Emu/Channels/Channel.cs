using System;
using System.Runtime.CompilerServices;

namespace Spice86.Core.Emulator.Devices.Sound.Ymf262Emu.Channels;

/// <summary>
/// Emulates an OPL channel.
/// </summary>
internal abstract class Channel
{
    public const int CHD1_CHC1_CHB1_CHA1_FB3_CNT1_Offset = 0xC0;

    // Factor to convert between normalized amplitude to normalized
    // radians. The amplitude maximum is equivalent to 8*Pi radians.
    protected const double toPhase = 4;
    protected const int _2_KON1_BLOCK3_FNUMH2_Offset = 0xB0;
    protected const int FNUML8_Offset = 0xA0;

    public readonly int channelBaseAddress;
    protected readonly FmSynthesizer opl;
    protected double feedback0;
    protected double feedback1;
    protected int fnuml, fnumh, kon, block, cha, chb, chc, chd, fb, cnt;

    // Feedback rate in fractions of 2*Pi, normalized to (0,1): 
    // 0, Pi/16, Pi/8, Pi/4, Pi/2, Pi, 2*Pi, 4*Pi turns to be:
    protected static readonly double[] feedbackTable = { 0, 1 / 32d, 1 / 16d, 1 / 8d, 1 / 4d, 1 / 2d, 1, 2 };

    /// <summary>
    /// Initializes a new instance of the Channel class.
    /// </summary>
    /// <param name="baseAddress">Base address of the channel's registers.</param>
    /// <param name="opl">FmSynthesizer instance which owns the channel.</param>
    public Channel(int baseAddress, FmSynthesizer opl)
    {
        this.channelBaseAddress = baseAddress;
        this.opl = opl;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void GetFourChannelOutput(double channelOutput, Span<double> output)
    {
        if (this.opl.IsOpl3Mode == 0)
        {
            output.Fill(channelOutput);
        }
        else
        {
            output[0] = (this.cha == 1) ? channelOutput : 0;
            output[1] = (this.chb == 1) ? channelOutput : 0;
            output[2] = (this.chc == 1) ? channelOutput : 0;
            output[3] = (this.chd == 1) ? channelOutput : 0;
        }
    }
    /// <summary>
    /// Returns an array containing the channel's output values.
    /// </summary>
    /// <returns>Array containing the channel's output values.</returns>
    public abstract void GetChannelOutput(Span<double> output);
    /// <summary>
    /// Activates channel output.
    /// </summary>
    public abstract void KeyOn();
    /// <summary>
    /// Disables channel output.
    /// </summary>
    public abstract void KeyOff();
    /// <summary>
    /// Updates the state of all of the operators in the channel.
    /// </summary>
    public abstract void UpdateOperators();
    public void Update_2_KON1_BLOCK3_FNUMH2()
    {
        int _2_kon1_block3_fnumh2 = this.opl.registers[this.channelBaseAddress + _2_KON1_BLOCK3_FNUMH2_Offset];

        // Frequency Number (hi-register) and Block. These two registers, together with fnuml, 
        // sets the Channel´s base frequency;
        this.block = (_2_kon1_block3_fnumh2 & 0x1C) >> 2;
        this.fnumh = _2_kon1_block3_fnumh2 & 0x03;
        this.UpdateOperators();

        // Key On. If changed, calls Channel.keyOn() / keyOff().
        int newKon = (_2_kon1_block3_fnumh2 & 0x20) >> 5;
        if (newKon != this.kon)
        {
            if (newKon == 1)
                this.KeyOn();
            else
                this.KeyOff();

            this.kon = newKon;
        }
    }
    public void Update_FNUML8()
    {
        int fnuml8 = this.opl.registers[this.channelBaseAddress + FNUML8_Offset];
        // Frequency Number, low register.
        this.fnuml = fnuml8 & 0xFF;
        this.UpdateOperators();
    }
    public void Update_CHD1_CHC1_CHB1_CHA1_FB3_CNT1()
    {
        int chd1_chc1_chb1_cha1_fb3_cnt1 = this.opl.registers[this.channelBaseAddress + CHD1_CHC1_CHB1_CHA1_FB3_CNT1_Offset];
        this.chd = (chd1_chc1_chb1_cha1_fb3_cnt1 & 0x80) >> 7;
        this.chc = (chd1_chc1_chb1_cha1_fb3_cnt1 & 0x40) >> 6;
        this.chb = (chd1_chc1_chb1_cha1_fb3_cnt1 & 0x20) >> 5;
        this.cha = (chd1_chc1_chb1_cha1_fb3_cnt1 & 0x10) >> 4;
        this.fb = (chd1_chc1_chb1_cha1_fb3_cnt1 & 0x0E) >> 1;
        this.cnt = chd1_chc1_chb1_cha1_fb3_cnt1 & 0x01;
        this.UpdateOperators();
    }
    /// <summary>
    /// Updates the state of the channel.
    /// </summary>
    public void UpdateChannel()
    {
        this.Update_2_KON1_BLOCK3_FNUMH2();
        this.Update_FNUML8();
        this.Update_CHD1_CHC1_CHB1_CHA1_FB3_CNT1();
    }
}
