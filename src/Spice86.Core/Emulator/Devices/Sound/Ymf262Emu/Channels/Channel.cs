namespace Spice86.Core.Emulator.Devices.Sound.Ymf262Emu.Channels;
using System;
using System.Runtime.CompilerServices;

/// <summary>
/// Emulates an OPL channel.
/// </summary>
internal abstract class Channel
{
    public const int Chd1Chc1Chb1Cha1Fb3Cnt1Offset = 0xC0;

    // Factor to convert between normalized amplitude to normalized
    // radians. The amplitude maximum is equivalent to 8*Pi radians.
    protected const double ToPhase = 4;
    protected const int _2_KON1_BLOCK3_FNUMH2_Offset = 0xB0;
    protected const int Fnuml8Offset = 0xA0;

    public readonly int ChannelBaseAddress;
    protected readonly FmSynthesizer Opl;
    protected double Feedback0;
    protected double Feedback1;
    protected int Fnuml, Fnumh, Kon, Block, Cha, Chb, Chc, Chd, Fb, Cnt;

    // Feedback rate in fractions of 2*Pi, normalized to (0,1): 
    // 0, Pi/16, Pi/8, Pi/4, Pi/2, Pi, 2*Pi, 4*Pi turns to be:
    protected static readonly double[] FeedbackTable = { 0, 1 / 32d, 1 / 16d, 1 / 8d, 1 / 4d, 1 / 2d, 1, 2 };

    /// <summary>
    /// Initializes a new instance of the Channel class.
    /// </summary>
    /// <param name="baseAddress">Base address of the channel's registers.</param>
    /// <param name="opl">FmSynthesizer instance which owns the channel.</param>
    public Channel(int baseAddress, FmSynthesizer opl)
    {
        ChannelBaseAddress = baseAddress;
        this.Opl = opl;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void GetFourChannelOutput(double channelOutput, Span<double> output)
    {
        if (Opl.IsOpl3Mode == 0)
        {
            output.Fill(channelOutput);
        }
        else
        {
            output[0] = (Cha == 1) ? channelOutput : 0;
            output[1] = (Chb == 1) ? channelOutput : 0;
            output[2] = (Chc == 1) ? channelOutput : 0;
            output[3] = (Chd == 1) ? channelOutput : 0;
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
        int _2_kon1_block3_fnumh2 = Opl.Registers[ChannelBaseAddress + _2_KON1_BLOCK3_FNUMH2_Offset];

        // Frequency Number (hi-register) and Block. These two registers, together with fnuml, 
        // sets the Channel´s base frequency;
        Block = (_2_kon1_block3_fnumh2 & 0x1C) >> 2;
        Fnumh = _2_kon1_block3_fnumh2 & 0x03;
        UpdateOperators();

        // Key On. If changed, calls Channel.keyOn() / keyOff().
        int newKon = (_2_kon1_block3_fnumh2 & 0x20) >> 5;
        if (newKon != Kon)
        {
            if (newKon == 1) {
                KeyOn();
            } else {
                KeyOff();
            }

            Kon = newKon;
        }
    }
    
    public void Update_FNUML8()
    {
        int fnuml8 = Opl.Registers[ChannelBaseAddress + Fnuml8Offset];
        // Frequency Number, low register.
        Fnuml = fnuml8 & 0xFF;
        UpdateOperators();
    }
    
    public void Update_CHD1_CHC1_CHB1_CHA1_FB3_CNT1()
    {
        int chd1Chc1Chb1Cha1Fb3Cnt1 = Opl.Registers[ChannelBaseAddress + Chd1Chc1Chb1Cha1Fb3Cnt1Offset];
        Chd = (chd1Chc1Chb1Cha1Fb3Cnt1 & 0x80) >> 7;
        Chc = (chd1Chc1Chb1Cha1Fb3Cnt1 & 0x40) >> 6;
        Chb = (chd1Chc1Chb1Cha1Fb3Cnt1 & 0x20) >> 5;
        Cha = (chd1Chc1Chb1Cha1Fb3Cnt1 & 0x10) >> 4;
        Fb = (chd1Chc1Chb1Cha1Fb3Cnt1 & 0x0E) >> 1;
        Cnt = chd1Chc1Chb1Cha1Fb3Cnt1 & 0x01;
        UpdateOperators();
    }
    
    /// <summary>
    /// Updates the state of the channel.
    /// </summary>
    public void UpdateChannel()
    {
        Update_2_KON1_BLOCK3_FNUMH2();
        Update_FNUML8();
        Update_CHD1_CHC1_CHB1_CHA1_FB3_CNT1();
    }
}
