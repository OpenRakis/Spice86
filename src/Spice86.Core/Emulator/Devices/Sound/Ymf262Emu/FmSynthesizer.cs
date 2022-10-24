using System;
using System.Runtime.CompilerServices;

namespace Spice86.Core.Emulator.Devices.Sound.Ymf262Emu;

/// <summary>
/// Emulates a YMF262 OPL3 device.
/// </summary>
public sealed class FmSynthesizer
{
    private const double tremoloDepth0 = -1;
    private const double tremoloDepth1 = -4.8;
    private readonly double tremoloIncrement0;
    private readonly double tremoloIncrement1;
    private readonly int tremoloTableLength;

    /// <summary>
    /// Initializes a new instance of the <see cref="FmSynthesizer"/> class.
    /// </summary>
    /// <param name="sampleRate">Sample rate in Hz of the generated waveform data.</param>
    public FmSynthesizer(int sampleRate = 44100)
    {
        if (sampleRate < 1000) {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        this.SampleRate = sampleRate;

        this.tremoloTableLength = (int)(sampleRate / TremoloFrequency);
        this.tremoloIncrement0 = this.CalculateIncrement(tremoloDepth0, 0, 1 / (2 * TremoloFrequency));
        this.tremoloIncrement1 = this.CalculateIncrement(tremoloDepth1, 0, 1 / (2 * TremoloFrequency));

        this.InitializeOperators();
        this.InitializeChannels2op();
        this.InitializeChannels4op();
        this.InitializeChannels();
        this.highHatOperator = new Operators.HighHat(this);
        this.tomTomOperator = new Operators.Operator(0x12, this);
        this.topCymbalOperator = new Operators.TopCymbal(this);
        this.bassDrumChannel = new Channels.BassDrum(this);
        this.snareDrumOperator = new Operators.SnareDrum(this);
        this.highHatSnareDrumChannel = new Channels.RhythmChannel(7, this.highHatOperator, this.snareDrumOperator, this);
        this.tomTomTopCymbalChannel = new Channels.RhythmChannel(8, this.tomTomOperator, this.topCymbalOperator, this);
    }

    /// <summary>
    /// Gets the sample rate of the output waveform data in Hz.
    /// </summary>
    public int SampleRate { get; }

    /// <summary>
    /// Fills <paramref name="buffer"/> with 16-bit mono samples.
    /// </summary>
    /// <param name="buffer">Buffer to fill with 16-bit waveform data.</param>
    public void GetData(Span<short> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = (short)(this.GetNextSample() * 32767);
    }
    /// <summary>
    /// Fills <paramref name="buffer"/> with 32-bit mono samples.
    /// </summary>
    /// <param name="buffer">Buffer to fill with 32-bit waveform data.</param>
    public void GetData(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++) {
            buffer[i] = (float)this.GetNextSample();
        }
    }

    /// <summary>
    /// Writes a value to one of the emulated device's registers.
    /// </summary>
    /// <param name="array">Register array (may be 0 or 1).</param>
    /// <param name="address">Register address in the range 0-0x200.</param>
    /// <param name="value">Register value.</param>
    public void SetRegisterValue(int array, int address, int value)
    {
        // The OPL3 has two registers arrays, each with adresses ranging
        // from 0x00 to 0xF5.
        // This emulator uses one array, with the two original register arrays
        // starting at 0x00 and at 0x100.
        int registerAddress = (array << 8) | address;
        // If the address is out of the OPL3 memory map, returns.
        if (registerAddress < 0 || registerAddress >= 0x200)
            return;

        this.registers[registerAddress] = value;
        switch (address & 0xE0)
        {
            // The first 3 bits masking gives the type of the register by using its base address:
            // 0x00, 0x20, 0x40, 0x60, 0x80, 0xA0, 0xC0, 0xE0 
            // When it is needed, we further separate the register type inside each base address,
            // which is the case of 0x00 and 0xA0.

            // Through out this emulator we will use the same name convention to
            // reference a byte with several bit registers.
            // The name of each bit register will be followed by the number of bits
            // it occupies inside the byte. 
            // Numbers without accompanying names are unused bits.
            case 0x00:
                // Unique registers for the entire OPL3:                
                if (array == 1)
                {
                    if (address == 0x04) {
                        this.Update_2_CONNECTIONSEL6();
                    } else if (address == 0x05) {
                        this.Update_7_NEW1();
                    }
                }
                else if (address == 0x08)
                {
                    this.Update_1_NTS1_6();
                }
                break;

            case 0xA0:
                // 0xBD is a control register for the entire OPL3:
                if (address == 0xBD)
                {
                    if (array == 0)
                        this.Update_DAM1_DVB1_RYT1_BD1_SD1_TOM1_TC1_HH1();
                    break;
                }
                // Registers for each channel are in A0-A8, B0-B8, C0-C8, in both register arrays.
                // 0xB0...0xB8 keeps kon,block,fnum(h) for each channel.
                if ((address & 0xF0) == 0xB0 && address <= 0xB8)
                {
                    // If the address is in the second register array, adds 9 to the channel number.
                    // The channel number is given by the last four bits, like in A0,...,A8.
                    this.channels[array, address & 0x0F].Update_2_KON1_BLOCK3_FNUMH2();
                    break;
                }
                // 0xA0...0xA8 keeps fnum(l) for each channel.
                if ((address & 0xF0) == 0xA0 && address <= 0xA8)
                    this.channels[array, address & 0x0F].Update_FNUML8();
                break;
            // 0xC0...0xC8 keeps cha,chb,chc,chd,fb,cnt for each channel:
            case 0xC0:
                if (address <= 0xC8)
                    this.channels[array, address & 0x0F].Update_CHD1_CHC1_CHB1_CHA1_FB3_CNT1();
                break;

            // Registers for each of the 36 Operators:
            default:
                int operatorOffset = address & 0x1F;
                if (this.operators[array, operatorOffset] == null)
                    break;
                switch (address & 0xE0)
                {
                    // 0x20...0x35 keeps am,vib,egt,ksr,mult for each operator:                
                    case 0x20:
                        this.operators[array, operatorOffset].Update_AM1_VIB1_EGT1_KSR1_MULT4();
                        break;
                    // 0x40...0x55 keeps ksl,tl for each operator: 
                    case 0x40:
                        this.operators[array, operatorOffset].Update_KSL2_TL6();
                        break;
                    // 0x60...0x75 keeps ar,dr for each operator: 
                    case 0x60:
                        this.operators[array, operatorOffset].Update_AR4_DR4();
                        break;
                    // 0x80...0x95 keeps sl,rr for each operator:
                    case 0x80:
                        this.operators[array, operatorOffset].Update_SL4_RR4();
                        break;
                    // 0xE0...0xF5 keeps ws for each operator:
                    case 0xE0:
                        this.operators[array, operatorOffset].Update_5_WS3();
                        break;
                }
                break;
        }
    }

    internal double CalculateIncrement(double begin, double end, double period) => (end - begin) / this.SampleRate * (1 / period);

    private double GetNextSample()
    {
        unsafe
        {
            Span<double> channelOutput = stackalloc double[4];

            var outputBuffer = stackalloc double[4] { 0, 0, 0, 0 };

            // If IsOpl3Mode = 0, use OPL2 mode with 9 channels. If IsOpl3Mode = 1, use OPL3 18 channels;
            for (int array = 0; array < (this.IsOpl3Mode + 1); array++)
            {
                for (int channelNumber = 0; channelNumber < 9; channelNumber++)
                {
                    // Reads output from each OPL3 channel, and accumulates it in the output buffer:
                    this.channels[array, channelNumber].GetChannelOutput(channelOutput);
                    for (int i = 0; i < channelOutput.Length; i++) {
                        outputBuffer[i] += channelOutput[i];
                    }
                }
            }

            double* output = stackalloc double[4];

            const double ratio = 1.0 / 18.0;

            // Normalizes the output buffer after all channels have been added,
            // with a maximum of 18 channels,
            // and multiplies it to get the 16 bit signed output.
            for (int i = 0; i < 4; i++) {
                output[i] = (float)(outputBuffer[i] * ratio);
            }

            // Advances the OPL3-wide vibrato index, which is used by 
            // PhaseGenerator.getPhase() in each Operator.
            this.vibratoIndex++;
            if (this.vibratoIndex >= VibratoGenerator.Length) {
                this.vibratoIndex = 0;
            }
            // Advances the OPL3-wide tremolo index, which is used by 
            // EnvelopeGenerator.getEnvelope() in each Operator.
            this.tremoloIndex++;
            if (this.tremoloIndex >= this.tremoloTableLength) {
                this.tremoloIndex = 0;
            }

            return (float)(output[0] + output[1] + output[2] + output[3]);
        }
    }

    internal double GetTremoloValue(int dam, int i)
    {
        if (i < this.tremoloTableLength / 2)
        {
            if (dam == 0) {
                return tremoloDepth0 + (this.tremoloIncrement0 * i);
            } else {
                return tremoloDepth1 + (this.tremoloIncrement1 * i);
            }
        }
        else
        {
            if (dam == 0)
                return -tremoloIncrement0 * i;
            else
                return -tremoloIncrement1 * i;
        }
    }
    private void InitializeOperators()
    {
        // The YMF262 has 36 operators:
        for (int array = 0; array < 2; array++)
        {
            for (int group = 0; group <= 0x10; group += 8)
            {
                for (int offset = 0; offset < 6; offset++)
                {
                    int baseAddress = (array << 8) | (group + offset);
                    operators[array, group + offset] = new Operators.Operator(baseAddress, this);
                }
            }
        }

        // Save operators when they are in non-rhythm mode:
        // Channel 7:
        highHatOperatorInNonRhythmMode = operators[0, 0x11];
        snareDrumOperatorInNonRhythmMode = operators[0, 0x14];
        // Channel 8:
        tomTomOperatorInNonRhythmMode = operators[0, 0x12];
        topCymbalOperatorInNonRhythmMode = operators[0, 0x15];
    }
    private void InitializeChannels2op()
    {
        // The YMF262 has 18 2-op channels.
        // Each 2-op channel can be at a serial or parallel operator configuration:
        for (int array = 0; array < 2; array++)
        {
            for (int channelNumber = 0; channelNumber < 3; channelNumber++)
            {
                int baseAddress = (array << 8) | channelNumber;
                // Channels 1, 2, 3 -> Operator offsets 0x0,0x3; 0x1,0x4; 0x2,0x5
                channels2op[array, channelNumber] = new Channels.Channel2(baseAddress, operators[array, channelNumber], operators[array, channelNumber + 0x3], this);
                // Channels 4, 5, 6 -> Operator offsets 0x8,0xB; 0x9,0xC; 0xA,0xD
                channels2op[array, channelNumber + 3] = new Channels.Channel2(baseAddress + 3, operators[array, channelNumber + 0x8], operators[array, channelNumber + 0xB], this);
                // Channels 7, 8, 9 -> Operators 0x10,0x13; 0x11,0x14; 0x12,0x15
                channels2op[array, channelNumber + 6] = new Channels.Channel2(baseAddress + 6, operators[array, channelNumber + 0x10], operators[array, channelNumber + 0x13], this);
            }
        }
    }
    private void InitializeChannels4op()
    {
        // The YMF262 has 3 4-op channels in each array:
        for (int array = 0; array < 2; array++)
        {
            for (int channelNumber = 0; channelNumber < 3; channelNumber++)
            {
                int baseAddress = (array << 8) | channelNumber;
                // Channels 1, 2, 3 -> Operators 0x0,0x3,0x8,0xB; 0x1,0x4,0x9,0xC; 0x2,0x5,0xA,0xD;
                channels4op[array, channelNumber] = new Channels.Channel4(baseAddress, operators[array, channelNumber], operators[array, channelNumber + 0x3], operators[array, channelNumber + 0x8], operators[array, channelNumber + 0xB], this);
            }
        }
    }
    private void InitializeChannels()
    {
        // Channel is an abstract class that can be a 2-op, 4-op, rhythm or disabled channel, 
        // depending on the OPL3 configuration at the time.
        // channels[] inits as a 2-op serial channel array:
        for (int array = 0; array < 2; array++)
        {
            for (int i = 0; i < 9; i++)
                channels[array, i] = channels2op[array, i];
        }
    }
    private void Update_1_NTS1_6()
    {
        int _1_nts1_6 = registers[_1_NTS1_6_Offset];
        // Note Selection. This register is used in Channel.updateOperators() implementations,
        // to calculate the channel´s Key Scale Number.
        // The value of the actual envelope rate follows the value of
        // OPL3.nts,Operator.keyScaleNumber and Operator.ksr
        nts = (_1_nts1_6 & 0x40) >> 6;
    }
    private void Update_DAM1_DVB1_RYT1_BD1_SD1_TOM1_TC1_HH1()
    {
        int dam1_dvb1_ryt1_bd1_sd1_tom1_tc1_hh1 = registers[DAM1_DVB1_RYT1_BD1_SD1_TOM1_TC1_HH1_Offset];
        // Depth of amplitude. This register is used in EnvelopeGenerator.getEnvelope();
        dam = (dam1_dvb1_ryt1_bd1_sd1_tom1_tc1_hh1 & 0x80) >> 7;

        // Depth of vibrato. This register is used in PhaseGenerator.getPhase();
        dvb = (dam1_dvb1_ryt1_bd1_sd1_tom1_tc1_hh1 & 0x40) >> 6;

        int new_ryt = (dam1_dvb1_ryt1_bd1_sd1_tom1_tc1_hh1 & 0x20) >> 5;
        if (new_ryt != ryt)
        {
            ryt = new_ryt;
            SetRhythmMode();
        }

        int new_bd = (dam1_dvb1_ryt1_bd1_sd1_tom1_tc1_hh1 & 0x10) >> 4;
        if (new_bd != bd)
        {
            bd = new_bd;
            if (bd == 1)
            {
                bassDrumChannel.op1.KeyOn();
                bassDrumChannel.op2.KeyOn();
            }
        }

        int new_sd = (dam1_dvb1_ryt1_bd1_sd1_tom1_tc1_hh1 & 0x08) >> 3;
        if (new_sd != sd)
        {
            sd = new_sd;
            if (sd == 1) snareDrumOperator.KeyOn();
        }

        int new_tom = (dam1_dvb1_ryt1_bd1_sd1_tom1_tc1_hh1 & 0x04) >> 2;
        if (new_tom != tom)
        {
            tom = new_tom;
            if (tom == 1) tomTomOperator.KeyOn();
        }

        int new_tc = (dam1_dvb1_ryt1_bd1_sd1_tom1_tc1_hh1 & 0x02) >> 1;
        if (new_tc != tc)
        {
            tc = new_tc;
            if (tc == 1) topCymbalOperator.KeyOn();
        }

        int new_hh = dam1_dvb1_ryt1_bd1_sd1_tom1_tc1_hh1 & 0x01;
        if (new_hh != hh)
        {
            hh = new_hh;
            if (hh == 1) highHatOperator.KeyOn();
        }
    }
    private void Update_7_NEW1()
    {
        int _7_new1 = registers[_7_NEW1_Offset];
        // OPL2/OPL3 mode selection. This register is used in 
        // OPL3.read(), OPL3.write() and Operator.getOperatorOutput();
        IsOpl3Mode = (_7_new1 & 0x01);
        if (IsOpl3Mode == 1) {
            SetEnabledChannels();
        }

        Set4opConnections();
    }
    private void SetEnabledChannels()
    {
        for (int array = 0; array < 2; array++)
        {
            for (int i = 0; i < 9; i++)
            {
                int baseAddress = channels[array, i].channelBaseAddress;
                registers[baseAddress + Channels.Channel.CHD1_CHC1_CHB1_CHA1_FB3_CNT1_Offset] |= 0xF0;
                channels[array, i].Update_CHD1_CHC1_CHB1_CHA1_FB3_CNT1();
            }
        }
    }
    private void Update_2_CONNECTIONSEL6()
    {
        // This method is called only if IsOpl3Mode is set.
        int _2_connectionsel6 = registers[_2_CONNECTIONSEL6_Offset];
        // 2-op/4-op channel selection. This register is used here to configure the OPL3.channels[] array.
        connectionsel = (_2_connectionsel6 & 0x3F);
        Set4opConnections();
    }
    private void Set4opConnections()
    {
        var disabledChannel = new Channels.NullChannel(this);

        // bits 0, 1, 2 sets respectively 2-op channels (1,4), (2,5), (3,6) to 4-op operation.
        // bits 3, 4, 5 sets respectively 2-op channels (10,13), (11,14), (12,15) to 4-op operation.
        for (int array = 0; array < 2; array++)
        {
            for (int i = 0; i < 3; i++)
            {
                if (IsOpl3Mode == 1)
                {
                    int shift = array * 3 + i;
                    int connectionBit = (connectionsel >> shift) & 0x01;
                    if (connectionBit == 1)
                    {
                        channels[array, i] = channels4op[array, i];
                        channels[array, i + 3] = disabledChannel;
                        channels[array, i].UpdateChannel();
                        continue;
                    }
                }
                channels[array, i] = channels2op[array, i];
                channels[array, i + 3] = channels2op[array, i + 3];
                channels[array, i].UpdateChannel();
                channels[array, i + 3].UpdateChannel();
            }
        }
    }
    private void SetRhythmMode()
    {
        if (ryt == 1)
        {
            channels[0, 6] = bassDrumChannel;
            channels[0, 7] = highHatSnareDrumChannel;
            channels[0, 8] = tomTomTopCymbalChannel;
            operators[0, 0x11] = highHatOperator;
            operators[0, 0x14] = snareDrumOperator;
            operators[0, 0x12] = tomTomOperator;
            operators[0, 0x15] = topCymbalOperator;
        }
        else
        {
            for (int i = 6; i <= 8; i++) {
                channels[0, i] = channels2op[0, i];
            }
            if(highHatOperatorInNonRhythmMode is not null) {
                operators[0, 0x11] = highHatOperatorInNonRhythmMode;
            }
            if(snareDrumOperatorInNonRhythmMode is not null) {
                operators[0, 0x14] = snareDrumOperatorInNonRhythmMode;
            }
            if(tomTomOperatorInNonRhythmMode is not null) {
                operators[0, 0x12] = tomTomOperatorInNonRhythmMode;
            }
            if(topCymbalOperatorInNonRhythmMode is not null) {
                operators[0, 0x15] = topCymbalOperatorInNonRhythmMode;
            }
        }
        for (int i = 6; i <= 8; i++) {
            channels[0, i].UpdateChannel();
        }
    }

    internal readonly int[] registers = new int[0x200];
    internal readonly Operators.HighHat highHatOperator;
    internal readonly Operators.SnareDrum snareDrumOperator;
    internal readonly Operators.Operator tomTomOperator;
    internal readonly Operators.TopCymbal topCymbalOperator;
    internal int nts, dam, dvb, ryt, bd, sd, tom, tc, hh, IsOpl3Mode, connectionsel;
    internal int vibratoIndex, tremoloIndex;

    private const double TremoloFrequency = 3.7;
    private const int _1_NTS1_6_Offset = 0x08;
    private const int DAM1_DVB1_RYT1_BD1_SD1_TOM1_TC1_HH1_Offset = 0xBD;
    private const int _7_NEW1_Offset = 0x105;
    private const int _2_CONNECTIONSEL6_Offset = 0x104;

    // The YMF262 has 36 operators.
    private readonly Operators.Operator[,] operators = new Operators.Operator[2, 0x20];
    // The YMF262 has 3 4-op channels in each array.
    private readonly Channels.Channel4[,] channels4op = new Channels.Channel4[2, 3];
    private readonly Channels.Channel[,] channels = new Channels.Channel[2, 9];
    private readonly Channels.BassDrum bassDrumChannel;
    private readonly Channels.RhythmChannel highHatSnareDrumChannel;
    private readonly Channels.RhythmChannel tomTomTopCymbalChannel;
    private Operators.Operator? highHatOperatorInNonRhythmMode;
    private Operators.Operator? snareDrumOperatorInNonRhythmMode;
    private Operators.Operator? tomTomOperatorInNonRhythmMode;
    private Operators.Operator? topCymbalOperatorInNonRhythmMode;

    // The YMF262 has 18 2-op channels.
    // Each 2-op channel can be at a serial or parallel operator configuration.
    private static readonly Channels.Channel2[,] channels2op = new Channels.Channel2[2, 9];
}
