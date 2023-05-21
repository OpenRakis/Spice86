namespace Spice86.Core.Emulator.Devices.Sound.Ymf262Emu;

using Spice86.Core.Emulator.Devices.Sound.Ymf262Emu.Channels;
using Spice86.Core.Emulator.Devices.Sound.Ymf262Emu.Operators;

/// <summary>
/// Emulates a YMF262 OPL3 device.
/// </summary>
public sealed class FmSynthesizer {
    private const double TremoloDepth0 = -1;
    private const double TremoloDepth1 = -4.8;
    private readonly double _tremoloIncrement0;
    private readonly double _tremoloIncrement1;
    private readonly int _tremoloTableLength;
    
    internal readonly int[] Registers = new int[0x200];
    internal readonly HighHat? HighHatOperator;
    internal readonly SnareDrum? SnareDrumOperator;
    internal readonly Operator? TomTomOperator;
    internal readonly TopCymbal? TopCymbalOperator;
    internal int Nts, Dam, Dvb, Ryt, Bd, Sd, Tom, Tc, Hh, IsOpl3Mode, Connectionsel;
    internal int VibratoIndex, TremoloIndex;

    private const double TremoloFrequency = 3.7;
    private const int _1_NTS1_6_Offset = 0x08;
    private const int Dam1Dvb1Ryt1Bd1Sd1Tom1Tc1Hh1Offset = 0xBD;
    private const int _7_NEW1_Offset = 0x105;
    private const int _2_CONNECTIONSEL6_Offset = 0x104;

    // The YMF262 has 36 operators.
    private readonly Operator?[,] _operators = new Operator[2, 0x20];
    // The YMF262 has 3 4-op channels in each array.
    private readonly Channel4[,] _channels4Op = new Channel4[2, 3];
    private readonly Channel[,] _channels = new Channel[2, 9];
    private readonly BassDrum _bassDrumChannel;
    private readonly RhythmChannel _highHatSnareDrumChannel;
    private readonly RhythmChannel _tomTomTopCymbalChannel;
    private Operator? _highHatOperatorInNonRhythmMode;
    private Operator? _snareDrumOperatorInNonRhythmMode;
    private Operator? _tomTomOperatorInNonRhythmMode;
    private Operator? _topCymbalOperatorInNonRhythmMode;

    // The YMF262 has 18 2-op channels.
    // Each 2-op channel can be at a serial or parallel operator configuration.
    private static readonly Channel2[,] Channels2Op = new Channel2[2, 9];

    /// <summary>
    /// Initializes a new instance of the <see cref="FmSynthesizer"/> class.
    /// </summary>
    /// <param name="sampleRate">Sample rate in Hz of the generated waveform data.</param>
    public FmSynthesizer(int sampleRate = 44100) {
        if (sampleRate < 1000) {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        SampleRate = sampleRate;

        _tremoloTableLength = (int)(sampleRate / TremoloFrequency);
        _tremoloIncrement0 = CalculateIncrement(TremoloDepth0, 0, 1 / (2 * TremoloFrequency));
        _tremoloIncrement1 = CalculateIncrement(TremoloDepth1, 0, 1 / (2 * TremoloFrequency));

        InitializeOperators();
        InitializeChannels2Op();
        InitializeChannels4Op();
        InitializeChannels();
        HighHatOperator = new HighHat(this);
        TomTomOperator = new Operator(0x12, this);
        TopCymbalOperator = new TopCymbal(this);
        _bassDrumChannel = new BassDrum(this);
        SnareDrumOperator = new SnareDrum(this);
        _highHatSnareDrumChannel = new RhythmChannel(7, HighHatOperator, SnareDrumOperator, this);
        _tomTomTopCymbalChannel = new RhythmChannel(8, TomTomOperator, TopCymbalOperator, this);
    }

    /// <summary>
    /// Gets the sample rate of the output waveform data in Hz.
    /// </summary>
    public int SampleRate { get; }

    /// <summary>
    /// Fills <paramref name="buffer"/> with 16-bit mono samples.
    /// </summary>
    /// <param name="buffer">Buffer to fill with 16-bit waveform data.</param>
    public void GetData(Span<short> buffer) {
        for (int i = 0; i < buffer.Length; i++) {
            buffer[i] = (short)(GetNextSample() * 32767);
        }
    }
    /// <summary>
    /// Fills <paramref name="buffer"/> with 32-bit mono samples.
    /// </summary>
    /// <param name="buffer">Buffer to fill with 32-bit waveform data.</param>
    public void GetData(Span<float> buffer) {
        for (int i = 0; i < buffer.Length; i++) {
            buffer[i] = (float)GetNextSample();
        }
    }

    /// <summary>
    /// Writes a value to one of the emulated device's registers.
    /// </summary>
    /// <param name="array">Register array (may be 0 or 1).</param>
    /// <param name="address">Register address in the range 0-0x200.</param>
    /// <param name="value">Register value.</param>
    public void SetRegisterValue(int array, int address, int value) {
        // The OPL3 has two registers arrays, each with adresses ranging
        // from 0x00 to 0xF5.
        // This emulator uses one array, with the two original register arrays
        // starting at 0x00 and at 0x100.
        int registerAddress = (array << 8) | address;
        // If the address is out of the OPL3 memory map, returns.
        if (registerAddress is < 0 or >= 0x200) {
            return;
        }

        Registers[registerAddress] = value;
        switch (address & 0xE0) {
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
                if (array == 1) {
                    if (address == 0x04) {
                        Update_2_CONNECTIONSEL6();
                    } else if (address == 0x05) {
                        Update_7_NEW1();
                    }
                }
                else if (address == 0x08) {
                    Update_1_NTS1_6();
                }
                break;

            case 0xA0:
                // 0xBD is a control register for the entire OPL3:
                if (address == 0xBD) {
                    if (array == 0) {
                        Update_DAM1_DVB1_RYT1_BD1_SD1_TOM1_TC1_HH1();
                    }

                    break;
                }
                // Registers for each channel are in A0-A8, B0-B8, C0-C8, in both register arrays.
                // 0xB0...0xB8 keeps kon,block,fnum(h) for each channel.
                if ((address & 0xF0) == 0xB0 && address <= 0xB8) {
                    // If the address is in the second register array, adds 9 to the channel number.
                    // The channel number is given by the last four bits, like in A0,...,A8.
                    _channels[array, address & 0x0F].Update_2_KON1_BLOCK3_FNUMH2();
                    break;
                }
                // 0xA0...0xA8 keeps fnum(l) for each channel.
                if ((address & 0xF0) == 0xA0 && address <= 0xA8) {
                    _channels[array, address & 0x0F].Update_FNUML8();
                }

                break;
            // 0xC0...0xC8 keeps cha,chb,chc,chd,fb,cnt for each channel:
            case 0xC0:
                if (address <= 0xC8) {
                    _channels[array, address & 0x0F].Update_CHD1_CHC1_CHB1_CHA1_FB3_CNT1();
                }

                break;

            // Registers for each of the 36 Operators:
            default:
                int operatorOffset = address & 0x1F;

                switch (address & 0xE0) {
                    // 0x20...0x35 keeps am,vib,egt,ksr,mult for each operator:                
                    case 0x20:
                        _operators[array, operatorOffset]?.Update_AM1_VIB1_EGT1_KSR1_MULT4();
                        break;
                    // 0x40...0x55 keeps ksl,tl for each operator: 
                    case 0x40:
                        _operators[array, operatorOffset]?.Update_KSL2_TL6();
                        break;
                    // 0x60...0x75 keeps ar,dr for each operator: 
                    case 0x60:
                        _operators[array, operatorOffset]?.Update_AR4_DR4();
                        break;
                    // 0x80...0x95 keeps sl,rr for each operator:
                    case 0x80:
                        _operators[array, operatorOffset]?.Update_SL4_RR4();
                        break;
                    // 0xE0...0xF5 keeps ws for each operator:
                    case 0xE0:
                        _operators[array, operatorOffset]?.Update_5_WS3();
                        break;
                }
                break;
        }
    }

    internal double CalculateIncrement(double begin, double end, double period) => (end - begin) / SampleRate * (1 / period);

    private double GetNextSample() {
        Span<double> channelOutput = stackalloc double[4];

        Span<double> outputBuffer = stackalloc double[4] { 0, 0, 0, 0 };

        // If IsOpl3Mode = 0, use OPL2 mode with 9 channels. If IsOpl3Mode = 1, use OPL3 18 channels;
        for (int array = 0; array < (IsOpl3Mode + 1); array++) {
            for (int channelNumber = 0; channelNumber < 9; channelNumber++) {
                // Reads output from each OPL3 channel, and accumulates it in the output buffer:
                _channels[array, channelNumber].GetChannelOutput(channelOutput);
                for (int i = 0; i < channelOutput.Length; i++) {
                    outputBuffer[i] += channelOutput[i];
                }
            }
        }

        Span<double> output = stackalloc double[4];

        const double ratio = 1.0 / 18.0;

        // Normalizes the output buffer after all channels have been added,
        // with a maximum of 18 channels,
        // and multiplies it to get the 16 bit signed output.
        for (int i = 0; i < 4; i++) {
            output[i] = (float)(outputBuffer[i] * ratio);
        }

        // Advances the OPL3-wide vibrato index, which is used by 
        // PhaseGenerator.getPhase() in each Operator.
        VibratoIndex++;
        if (VibratoIndex >= VibratoGenerator.Length) {
            VibratoIndex = 0;
        }
        // Advances the OPL3-wide tremolo index, which is used by 
        // EnvelopeGenerator.getEnvelope() in each Operator.
        TremoloIndex++;
        if (TremoloIndex >= _tremoloTableLength) {
            TremoloIndex = 0;
        }

        return (float)(output[0] + output[1] + output[2] + output[3]);
    }

    internal double GetTremoloValue(int damValue, int i) {
        if (i < _tremoloTableLength / 2) {
            if (damValue == 0) {
                return TremoloDepth0 + (_tremoloIncrement0 * i);
            } else {
                return TremoloDepth1 + (_tremoloIncrement1 * i);
            }
        }
        else {
            if (damValue == 0) {
                return -_tremoloIncrement0 * i;
            } else {
                return -_tremoloIncrement1 * i;
            }
        }
    }
    
    private void InitializeOperators() {
        // The YMF262 has 36 operators:
        for (int array = 0; array < 2; array++) {
            for (int group = 0; group <= 0x10; group += 8) {
                for (int offset = 0; offset < 6; offset++) {
                    int baseAddress = (array << 8) | (group + offset);
                    _operators[array, group + offset] = new Operator(baseAddress, this);
                }
            }
        }

        // Save operators when they are in non-rhythm mode:
        // Channel 7:
        _highHatOperatorInNonRhythmMode = _operators[0, 0x11];
        _snareDrumOperatorInNonRhythmMode = _operators[0, 0x14];
        // Channel 8:
        _tomTomOperatorInNonRhythmMode = _operators[0, 0x12];
        _topCymbalOperatorInNonRhythmMode = _operators[0, 0x15];
    }
    
    private void InitializeChannels2Op() {
        // The YMF262 has 18 2-op channels.
        // Each 2-op channel can be at a serial or parallel operator configuration:
        for (int array = 0; array < 2; array++) {
            for (int channelNumber = 0; channelNumber < 3; channelNumber++) {
                int baseAddress = (array << 8) | channelNumber;
                // Channels 1, 2, 3 -> Operator offsets 0x0,0x3; 0x1,0x4; 0x2,0x5
                Channels2Op[array, channelNumber] = new Channel2(baseAddress, _operators[array, channelNumber], _operators[array, channelNumber + 0x3], this);
                // Channels 4, 5, 6 -> Operator offsets 0x8,0xB; 0x9,0xC; 0xA,0xD
                Channels2Op[array, channelNumber + 3] = new Channel2(baseAddress + 3, _operators[array, channelNumber + 0x8], _operators[array, channelNumber + 0xB], this);
                // Channels 7, 8, 9 -> Operators 0x10,0x13; 0x11,0x14; 0x12,0x15
                Channels2Op[array, channelNumber + 6] = new Channel2(baseAddress + 6, _operators[array, channelNumber + 0x10], _operators[array, channelNumber + 0x13], this);
            }
        }
    }
    private void InitializeChannels4Op() {
        // The YMF262 has 3 4-op channels in each array:
        for (int array = 0; array < 2; array++) {
            for (int channelNumber = 0; channelNumber < 3; channelNumber++) {
                int baseAddress = (array << 8) | channelNumber;
                // Channels 1, 2, 3 -> Operators 0x0,0x3,0x8,0xB; 0x1,0x4,0x9,0xC; 0x2,0x5,0xA,0xD;
                _channels4Op[array, channelNumber] = new Channel4(baseAddress, _operators[array, channelNumber], _operators[array, channelNumber + 0x3], _operators[array, channelNumber + 0x8], _operators[array, channelNumber + 0xB], this);
            }
        }
    }
    
    private void InitializeChannels() {
        // Channel is an abstract class that can be a 2-op, 4-op, rhythm or disabled channel, 
        // depending on the OPL3 configuration at the time.
        // channels[] inits as a 2-op serial channel array:
        for (int array = 0; array < 2; array++) {
            for (int i = 0; i < 9; i++) {
                _channels[array, i] = Channels2Op[array, i];
            }
        }
    }
    
    private void Update_1_NTS1_6() {
        int _1_nts1_6 = Registers[_1_NTS1_6_Offset];
        // Note Selection. This register is used in Channel.updateOperators() implementations,
        // to calculate the channel´s Key Scale Number.
        // The value of the actual envelope rate follows the value of
        // OPL3.nts,Operator.keyScaleNumber and Operator.ksr
        Nts = (_1_nts1_6 & 0x40) >> 6;
    }
    
    private void Update_DAM1_DVB1_RYT1_BD1_SD1_TOM1_TC1_HH1() {
        int dam1Dvb1Ryt1Bd1Sd1Tom1Tc1Hh1 = Registers[Dam1Dvb1Ryt1Bd1Sd1Tom1Tc1Hh1Offset];
        // Depth of amplitude. This register is used in EnvelopeGenerator.getEnvelope();
        Dam = (dam1Dvb1Ryt1Bd1Sd1Tom1Tc1Hh1 & 0x80) >> 7;

        // Depth of vibrato. This register is used in PhaseGenerator.getPhase();
        Dvb = (dam1Dvb1Ryt1Bd1Sd1Tom1Tc1Hh1 & 0x40) >> 6;

        int newRyt = (dam1Dvb1Ryt1Bd1Sd1Tom1Tc1Hh1 & 0x20) >> 5;
        if (newRyt != Ryt) {
            Ryt = newRyt;
            SetRhythmMode();
        }

        int newBd = (dam1Dvb1Ryt1Bd1Sd1Tom1Tc1Hh1 & 0x10) >> 4;
        if (newBd != Bd) {
            Bd = newBd;
            if (Bd == 1) {
                _bassDrumChannel.Op1?.KeyOn();
                _bassDrumChannel.Op2?.KeyOn();
            }
        }

        int newSd = (dam1Dvb1Ryt1Bd1Sd1Tom1Tc1Hh1 & 0x08) >> 3;
        if (newSd != Sd) {
            Sd = newSd;
            if (Sd == 1) {
                SnareDrumOperator?.KeyOn();
            }
        }

        int newTom = (dam1Dvb1Ryt1Bd1Sd1Tom1Tc1Hh1 & 0x04) >> 2;
        if (newTom != Tom) {
            Tom = newTom;
            if (Tom == 1) {
                TomTomOperator?.KeyOn();
            }
        }

        int newTc = (dam1Dvb1Ryt1Bd1Sd1Tom1Tc1Hh1 & 0x02) >> 1;
        if (newTc != Tc) {
            Tc = newTc;
            if (Tc == 1) {
                TopCymbalOperator?.KeyOn();
            }
        }

        int newHh = dam1Dvb1Ryt1Bd1Sd1Tom1Tc1Hh1 & 0x01;
        if (newHh != Hh) {
            Hh = newHh;
            if (Hh == 1) {
                HighHatOperator?.KeyOn();
            }
        }
    }
    
    private void Update_7_NEW1() {
        int _7_new1 = Registers[_7_NEW1_Offset];
        // OPL2/OPL3 mode selection. This register is used in 
        // OPL3.read(), OPL3.write() and Operator.getOperatorOutput();
        IsOpl3Mode = (_7_new1 & 0x01);
        if (IsOpl3Mode == 1) {
            SetEnabledChannels();
        }

        Set4OpConnections();
    }
    
    private void SetEnabledChannels() {
        for (int array = 0; array < 2; array++) {
            for (int i = 0; i < 9; i++) {
                int baseAddress = _channels[array, i].ChannelBaseAddress;
                Registers[baseAddress + Channel.Chd1Chc1Chb1Cha1Fb3Cnt1Offset] |= 0xF0;
                _channels[array, i].Update_CHD1_CHC1_CHB1_CHA1_FB3_CNT1();
            }
        }
    }
    
    private void Update_2_CONNECTIONSEL6() {
        // This method is called only if IsOpl3Mode is set.
        int _2_connectionsel6 = Registers[_2_CONNECTIONSEL6_Offset];
        // 2-op/4-op channel selection. This register is used here to configure the OPL3.channels[] array.
        Connectionsel = (_2_connectionsel6 & 0x3F);
        Set4OpConnections();
    }
    
    private void Set4OpConnections() {
        var disabledChannel = new NullChannel(this);

        // bits 0, 1, 2 sets respectively 2-op channels (1,4), (2,5), (3,6) to 4-op operation.
        // bits 3, 4, 5 sets respectively 2-op channels (10,13), (11,14), (12,15) to 4-op operation.
        for (int array = 0; array < 2; array++) {
            for (int i = 0; i < 3; i++) {
                if (IsOpl3Mode == 1) {
                    int shift = (array * 3) + i;
                    int connectionBit = (Connectionsel >> shift) & 0x01;
                    if (connectionBit == 1) {
                        _channels[array, i] = _channels4Op[array, i];
                        _channels[array, i + 3] = disabledChannel;
                        _channels[array, i].UpdateChannel();
                        continue;
                    }
                }
                _channels[array, i] = Channels2Op[array, i];
                _channels[array, i + 3] = Channels2Op[array, i + 3];
                _channels[array, i].UpdateChannel();
                _channels[array, i + 3].UpdateChannel();
            }
        }
    }
    
    private void SetRhythmMode() {
        if (Ryt == 1) {
            _channels[0, 6] = _bassDrumChannel;
            _channels[0, 7] = _highHatSnareDrumChannel;
            _channels[0, 8] = _tomTomTopCymbalChannel;
            _operators[0, 0x11] = HighHatOperator;
            _operators[0, 0x14] = SnareDrumOperator;
            _operators[0, 0x12] = TomTomOperator;
            _operators[0, 0x15] = TopCymbalOperator;
        }
        else {
            for (int i = 6; i <= 8; i++) {
                _channels[0, i] = Channels2Op[0, i];
            }
            if(_highHatOperatorInNonRhythmMode is not null) {
                _operators[0, 0x11] = _highHatOperatorInNonRhythmMode;
            }
            if(_snareDrumOperatorInNonRhythmMode is not null) {
                _operators[0, 0x14] = _snareDrumOperatorInNonRhythmMode;
            }
            if(_tomTomOperatorInNonRhythmMode is not null) {
                _operators[0, 0x12] = _tomTomOperatorInNonRhythmMode;
            }
            if(_topCymbalOperatorInNonRhythmMode is not null) {
                _operators[0, 0x15] = _topCymbalOperatorInNonRhythmMode;
            }
        }
        for (int i = 6; i <= 8; i++) {
            _channels[0, i].UpdateChannel();
        }
    }
}
