namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

using Spice86.Audio.Backend;
using Spice86.Audio.Common;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.DeviceScheduler;

using System;

using EventHandler = Spice86.Core.Emulator.VM.DeviceScheduler.EventHandler;

/// <summary>
/// Constants, enums, nested types, static data, and instance field declarations
/// for the Sound Blaster emulation. Mirrors soundblaster.cpp file-scope definitions
/// and private/soundblaster.h class members.
/// </summary>
/// <remarks>
/// 2022-2025 The DOSBox Staging Team
/// </remarks>
public partial class SoundBlaster {
    private const int DmaBufSize = 1024;
    private const int DspBufSize = 64;
    private const int SbShift = 14;
    private const ushort SbShiftMask = (1 << SbShift) - 1;

    private const byte MinAdaptiveStepSize = 0; // max is 32767

    // It was common for games perform some initial checks
    // and resets on startup, resulting a rapid succession of resets.
    private const byte DspInitialResetLimit = 4;

    // The official guide states the following:
    // "Valid output rates range from 5000 to 45 000 Hz, inclusive."
    //
    // However, this statement is wrong as in actual reality the maximum
    // achievable sample rate is the native SB DAC rate of 45454 Hz, and
    // many programs use this highest rate. Limiting the max rate to 45000
    // Hz would result in a slightly out-of-tune, detuned pitch in such
    // programs.
    //
    // More details:
    // https://www.vogons.org/viewtopic.php?p=621717#p621717
    //
    // Ref:
    //   Sound Blaster Series Hardware Programming Guide,
    //   41h Set digitized sound output sampling rate, DSP Commands 6-15
    //   https://pdos.csail.mit.edu/6.828/2018/readings/hardware/SoundBlaster.pdf
    //
    private const int MinPlaybackRateHz = 5000;
    private const int NativeDacRateHz = 45454;
    private const ushort DefaultPlaybackRateHz = 22050;

    // -----------------------------------------------------------------------
    // soundblaster.cpp enums
    // -----------------------------------------------------------------------

    private enum DspState { Reset, ResetWait, Normal, HighSpeed }

    private enum DmaMode {
        None,
        Adpcm2Bit,
        Adpcm3Bit,
        Adpcm4Bit,
        Pcm8Bit,
        Pcm16Bit,
        Pcm16BitAliased
    }

    private enum FrameType {
        Mono,
        Stereo
    }

    private enum DspMode { None, Dac, Dma, DmaPause, DmaMasked }

    private enum EssType { None, Es1688 }

    private enum SbIrq { Irq8, Irq16, IrqMpu }

    private enum BlasterState : byte { WaitingForCommand, ReadingCommand }

    private enum TimingType { None, PerTick, PerFrame }

    /// <summary>
    /// Sound Blaster port offsets relative to the base address.
    /// </summary>
    public enum SoundBlasterPortOffset : byte {
        /// <summary>
        /// Mixer index register.
        /// </summary>
        MixerIndex = 0x04,

        /// <summary>
        /// Mixer data register.
        /// </summary>
        MixerData = 0x05,

        /// <summary>
        /// DSP reset register (read/write).
        /// </summary>
        DspReset = 0x06,

        /// <summary>
        /// DSP read data register.
        /// </summary>
        DspReadData = 0x0A,

        /// <summary>
        /// DSP write command/data register.
        /// </summary>
        DspWriteData = 0x0C,

        /// <summary>
        /// DSP write buffer status (bit 7 = buffer full).
        /// </summary>
        DspWriteStatus = 0x0C,

        /// <summary>
        /// DSP read buffer status (bit 7 = data available). Also acknowledges 8-bit IRQ.
        /// </summary>
        DspReadStatus = 0x0E,

        /// <summary>
        /// DSP 16-bit IRQ acknowledge.
        /// </summary>
        DspAck16Bit = 0x0F
    }

    /// <summary>
    /// DSP command codes for Sound Blaster.
    /// </summary>
    public enum DspCommand : byte {
        /// <summary>
        /// DSP status (SB 2.0/Pro only, not SB16). For SB16, this is ASP set mode register.
        /// </summary>
        DspStatusOrAspSetMode = 0x04,

        /// <summary>
        /// SB16 ASP set codec parameter.
        /// </summary>
        AspSetCodecParameter = 0x05,

        /// <summary>
        /// SB16 ASP get version.
        /// </summary>
        AspGetVersion = 0x08,

        /// <summary>
        /// SB16 ASP set register.
        /// </summary>
        AspSetRegister = 0x0E,

        /// <summary>
        /// SB16 ASP get register.
        /// </summary>
        AspGetRegister = 0x0F,

        /// <summary>
        /// Direct DAC output (8-bit).
        /// </summary>
        DirectDac = 0x10,

        /// <summary>
        /// Single cycle 8-bit DMA DAC.
        /// </summary>
        SingleCycle8BitDmaDac = 0x14,

        /// <summary>
        /// Single cycle 8-bit DMA DAC (Wari hack).
        /// </summary>
        SingleCycle8BitDmaDacWari = 0x15,

        /// <summary>
        /// Single cycle 2-bit ADPCM.
        /// </summary>
        SingleCycleAdpcm2Bit = 0x16,

        /// <summary>
        /// Single cycle 2-bit ADPCM with reference byte.
        /// </summary>
        SingleCycleAdpcm2BitRef = 0x17,

        /// <summary>
        /// Auto-init 8-bit DMA.
        /// </summary>
        AutoInit8BitDma = 0x1C,

        /// <summary>
        /// Creative Parrot - fake silent input.
        /// </summary>
        CreativeParrotInput = 0x20,

        /// <summary>
        /// Single cycle 8-bit DMA ADC.
        /// </summary>
        SingleCycle8BitDmaAdc = 0x24,

        /// <summary>
        /// Unimplemented input command.
        /// </summary>
        UnimplementedInput2C = 0x2C,

        /// <summary>
        /// Unimplemented MIDI I/O command.
        /// </summary>
        UnimplementedMidiIo30 = 0x30,

        /// <summary>
        /// Unimplemented MIDI I/O command.
        /// </summary>
        UnimplementedMidiIo31 = 0x31,

        /// <summary>
        /// Unimplemented MIDI UART command.
        /// </summary>
        UnimplementedMidiUart34 = 0x34,

        /// <summary>
        /// Unimplemented MIDI UART command.
        /// </summary>
        UnimplementedMidiUart35 = 0x35,

        /// <summary>
        /// Unimplemented MIDI UART command.
        /// </summary>
        UnimplementedMidiUart36 = 0x36,

        /// <summary>
        /// Unimplemented MIDI UART command.
        /// </summary>
        UnimplementedMidiUart37 = 0x37,

        /// <summary>
        /// Write to SB MIDI output.
        /// </summary>
        WriteMidiOutput = 0x38,

        /// <summary>
        /// Set time constant for playback rate.
        /// </summary>
        SetTimeConstant = 0x40,

        /// <summary>
        /// Set output sample rate (SB16).
        /// </summary>
        SetOutputSampleRate = 0x41,

        /// <summary>
        /// Set input sample rate (SB16, handled like 0x41).
        /// </summary>
        SetInputSampleRate = 0x42,

        /// <summary>
        /// Set DMA block size.
        /// </summary>
        SetDmaBlockSize = 0x48,

        /// <summary>
        /// Single cycle 4-bit ADPCM.
        /// </summary>
        SingleCycleAdpcm4Bit = 0x74,

        /// <summary>
        /// Single cycle 4-bit ADPCM with reference byte.
        /// </summary>
        SingleCycleAdpcm4BitRef = 0x75,

        /// <summary>
        /// Single cycle 3-bit (2.6-bit) ADPCM.
        /// </summary>
        SingleCycleAdpcm3Bit = 0x76,

        /// <summary>
        /// Single cycle 3-bit (2.6-bit) ADPCM with reference byte.
        /// </summary>
        SingleCycleAdpcm3BitRef = 0x77,

        /// <summary>
        /// Auto-init 4-bit ADPCM with reference byte.
        /// </summary>
        AutoInitAdpcm4BitRef = 0x7D,

        /// <summary>
        /// Unimplemented auto-init DMA ADPCM command.
        /// </summary>
        UnimplementedAutoInitAdpcm1F = 0x1F,

        /// <summary>
        /// Unimplemented auto-init DMA ADPCM command.
        /// </summary>
        UnimplementedAutoInitAdpcm7F = 0x7F,

        /// <summary>
        /// Silence DAC.
        /// </summary>
        SilenceDac = 0x80,

        /// <summary>
        /// Auto-init 8-bit DMA high speed (DSP 2.x/3.x).
        /// </summary>
        AutoInit8BitDmaHighSpeed = 0x90,

        /// <summary>
        /// Single cycle 8-bit DMA high speed DAC (DSP 2.x/3.x).
        /// </summary>
        SingleCycle8BitDmaHighSpeed = 0x91,

        /// <summary>
        /// Unimplemented input command (DSP 2.x/3.x).
        /// </summary>
        UnimplementedInput98 = 0x98,

        /// <summary>
        /// Unimplemented input command (DSP 2.x/3.x).
        /// </summary>
        UnimplementedInput99 = 0x99,

        /// <summary>
        /// Unimplemented input command.
        /// </summary>
        UnimplementedInputA0 = 0xA0,

        /// <summary>
        /// Unimplemented input command (DSP 3.x).
        /// </summary>
        UnimplementedInputA8 = 0xA8,

        /// <summary>
        /// Generic 8-bit DMA command (SB16).
        /// </summary>
        Generic8BitDmaB0 = 0xB0,

        /// <summary>
        /// Generic 8-bit DMA command (SB16).
        /// </summary>
        Generic8BitDmaB1 = 0xB1,

        /// <summary>
        /// Generic 8-bit DMA command (SB16).
        /// </summary>
        Generic8BitDmaB2 = 0xB2,

        /// <summary>
        /// Generic 8-bit DMA command (SB16).
        /// </summary>
        Generic8BitDmaB3 = 0xB3,

        /// <summary>
        /// Generic 8-bit DMA command (SB16).
        /// </summary>
        Generic8BitDmaB4 = 0xB4,

        /// <summary>
        /// Generic 8-bit DMA command (SB16).
        /// </summary>
        Generic8BitDmaB5 = 0xB5,

        /// <summary>
        /// Generic 8-bit DMA command (SB16).
        /// </summary>
        Generic8BitDmaB6 = 0xB6,

        /// <summary>
        /// Generic 8-bit DMA command (SB16).
        /// </summary>
        Generic8BitDmaB7 = 0xB7,

        /// <summary>
        /// Generic 8-bit DMA command (SB16).
        /// </summary>
        Generic8BitDmaB8 = 0xB8,

        /// <summary>
        /// Generic 8-bit DMA command (SB16).
        /// </summary>
        Generic8BitDmaB9 = 0xB9,

        /// <summary>
        /// Generic 8-bit DMA command (SB16).
        /// </summary>
        Generic8BitDmaBA = 0xBA,

        /// <summary>
        /// Generic 8-bit DMA command (SB16).
        /// </summary>
        Generic8BitDmaBB = 0xBB,

        /// <summary>
        /// Generic 8-bit DMA command (SB16).
        /// </summary>
        Generic8BitDmaBC = 0xBC,

        /// <summary>
        /// Generic 8-bit DMA command (SB16).
        /// </summary>
        Generic8BitDmaBD = 0xBD,

        /// <summary>
        /// Generic 8-bit DMA command (SB16).
        /// </summary>
        Generic8BitDmaBE = 0xBE,

        /// <summary>
        /// Generic 8-bit DMA command (SB16).
        /// </summary>
        Generic8BitDmaBF = 0xBF,

        /// <summary>
        /// Generic 16-bit DMA command (SB16).
        /// </summary>
        Generic16BitDmaC0 = 0xC0,

        /// <summary>
        /// Generic 16-bit DMA command (SB16).
        /// </summary>
        Generic16BitDmaC1 = 0xC1,

        /// <summary>
        /// Generic 16-bit DMA command (SB16).
        /// </summary>
        Generic16BitDmaC2 = 0xC2,

        /// <summary>
        /// Generic 16-bit DMA command (SB16).
        /// </summary>
        Generic16BitDmaC3 = 0xC3,

        /// <summary>
        /// Generic 16-bit DMA command (SB16).
        /// </summary>
        Generic16BitDmaC4 = 0xC4,

        /// <summary>
        /// Generic 16-bit DMA command (SB16).
        /// </summary>
        Generic16BitDmaC5 = 0xC5,

        /// <summary>
        /// Generic 16-bit DMA command (SB16).
        /// </summary>
        Generic16BitDmaC6 = 0xC6,

        /// <summary>
        /// Generic 16-bit DMA command (SB16).
        /// </summary>
        Generic16BitDmaC7 = 0xC7,

        /// <summary>
        /// Generic 16-bit DMA command (SB16).
        /// </summary>
        Generic16BitDmaC8 = 0xC8,

        /// <summary>
        /// Generic 16-bit DMA command (SB16).
        /// </summary>
        Generic16BitDmaC9 = 0xC9,

        /// <summary>
        /// Generic 16-bit DMA command (SB16).
        /// </summary>
        Generic16BitDmaCA = 0xCA,

        /// <summary>
        /// Generic 16-bit DMA command (SB16).
        /// </summary>
        Generic16BitDmaCB = 0xCB,

        /// <summary>
        /// Generic 16-bit DMA command (SB16).
        /// </summary>
        Generic16BitDmaCC = 0xCC,

        /// <summary>
        /// Generic 16-bit DMA command (SB16).
        /// </summary>
        Generic16BitDmaCD = 0xCD,

        /// <summary>
        /// Generic 16-bit DMA command (SB16).
        /// </summary>
        Generic16BitDmaCE = 0xCE,

        /// <summary>
        /// Generic 16-bit DMA command (SB16).
        /// </summary>
        Generic16BitDmaCF = 0xCF,

        /// <summary>
        /// Halt 8-bit DMA.
        /// </summary>
        Halt8BitDma = 0xD0,

        /// <summary>
        /// Enable speaker.
        /// </summary>
        EnableSpeaker = 0xD1,

        /// <summary>
        /// Disable speaker.
        /// </summary>
        DisableSpeaker = 0xD3,

        /// <summary>
        /// Continue 8-bit DMA.
        /// </summary>
        Continue8BitDma = 0xD4,

        /// <summary>
        /// Halt 16-bit DMA (SB16).
        /// </summary>
        Halt16BitDma = 0xD5,

        /// <summary>
        /// Continue 16-bit DMA (SB16).
        /// </summary>
        Continue16BitDma = 0xD6,

        /// <summary>
        /// Get speaker status.
        /// </summary>
        GetSpeakerStatus = 0xD8,

        /// <summary>
        /// Exit auto-initialize 8-bit DMA.
        /// </summary>
        ExitAutoInit8Bit = 0xDA,

        /// <summary>
        /// Exit auto-initialize 16-bit DMA (SB16).
        /// </summary>
        ExitAutoInit16Bit = 0xD9,

        /// <summary>
        /// DSP identification (SB2.0+).
        /// </summary>
        DspIdentification = 0xE0,

        /// <summary>
        /// Get DSP version.
        /// </summary>
        GetDspVersion = 0xE1,

        /// <summary>
        /// Weird DMA identification write routine.
        /// </summary>
        DmaIdentification = 0xE2,

        /// <summary>
        /// Get DSP copyright string.
        /// </summary>
        GetDspCopyright = 0xE3,

        /// <summary>
        /// Write test register.
        /// </summary>
        WriteTestRegister = 0xE4,

        /// <summary>
        /// ESS detect/read config.
        /// </summary>
        EssDetectReadConfig = 0xE7,

        /// <summary>
        /// Read test register.
        /// </summary>
        ReadTestRegister = 0xE8,

        /// <summary>
        /// Trigger 8-bit IRQ.
        /// </summary>
        Trigger8BitIrq = 0xF2,

        /// <summary>
        /// Trigger 16-bit IRQ (SB16).
        /// </summary>
        Trigger16BitIrq = 0xF3,

        /// <summary>
        /// Undocumented command (pre-SB16 only).
        /// </summary>
        UndocumentedF8 = 0xF8,

        /// <summary>
        /// SB16 ASP unknown function.
        /// </summary>
        AspUnknownFunction = 0xF9
    }

    /// <summary>
    /// Sound Blaster mixer register indices.
    /// </summary>
    public enum MixerRegister : byte {
        /// <summary>
        /// Reset mixer.
        /// </summary>
        Reset = 0x00,

        /// <summary>
        /// Master volume (SB2 only).
        /// </summary>
        MasterVolumeSb2 = 0x02,

        /// <summary>
        /// DAC volume (SB Pro).
        /// </summary>
        DacVolumeSbPro = 0x04,

        /// <summary>
        /// FM output selection.
        /// </summary>
        FmOutputSelection = 0x06,

        /// <summary>
        /// CD audio volume (SB2 only).
        /// </summary>
        CdAudioVolumeSb2 = 0x08,

        /// <summary>
        /// Mic level (SB Pro) or DAC volume (SB2).
        /// </summary>
        MicLevelOrDacVolume = 0x0A,

        /// <summary>
        /// Output/stereo select and filter enable.
        /// </summary>
        OutputStereoSelect = 0x0E,

        /// <summary>
        /// Audio 1 play volume (ESS).
        /// </summary>
        Audio1PlayVolumeEss = 0x14,

        /// <summary>
        /// Master volume (SB Pro).
        /// </summary>
        MasterVolumeSbPro = 0x22,

        /// <summary>
        /// FM volume (SB Pro).
        /// </summary>
        FmVolumeSbPro = 0x26,

        /// <summary>
        /// CD audio volume (SB Pro).
        /// </summary>
        CdAudioVolumeSbPro = 0x28,

        /// <summary>
        /// Line-in volume (SB Pro).
        /// </summary>
        LineInVolumeSbPro = 0x2E,

        /// <summary>
        /// Master volume left (SB16).
        /// </summary>
        MasterVolumeLeft = 0x30,

        /// <summary>
        /// Master volume right (SB16).
        /// </summary>
        MasterVolumeRight = 0x31,

        /// <summary>
        /// DAC volume left (SB16) or master volume (ESS).
        /// </summary>
        DacVolumeLeftOrMasterEss = 0x32,

        /// <summary>
        /// DAC volume right (SB16).
        /// </summary>
        DacVolumeRight = 0x33,

        /// <summary>
        /// FM volume left (SB16).
        /// </summary>
        FmVolumeLeft = 0x34,

        /// <summary>
        /// FM volume right (SB16).
        /// </summary>
        FmVolumeRight = 0x35,

        /// <summary>
        /// CD audio volume left (SB16) or FM volume (ESS).
        /// </summary>
        CdAudioVolumeLeftOrFmEss = 0x36,

        /// <summary>
        /// CD audio volume right (SB16).
        /// </summary>
        CdAudioVolumeRight = 0x37,

        /// <summary>
        /// Line-in volume left (SB16) or CD audio volume (ESS).
        /// </summary>
        LineInVolumeLeftOrCdEss = 0x38,

        /// <summary>
        /// Line-in volume right (SB16).
        /// </summary>
        LineInVolumeRight = 0x39,

        /// <summary>
        /// Mic volume (SB16).
        /// </summary>
        MicVolume = 0x3A,

        /// <summary>
        /// Line volume (ESS).
        /// </summary>
        LineVolumeEss = 0x3E,

        /// <summary>
        /// ESS identification value (ES1488 and later).
        /// </summary>
        EssIdentification = 0x40,

        /// <summary>
        /// IRQ select register.
        /// </summary>
        IrqSelect = 0x80,

        /// <summary>
        /// DMA select register.
        /// </summary>
        DmaSelect = 0x81,

        /// <summary>
        /// IRQ status register.
        /// </summary>
        IrqStatus = 0x82
    }

    private class SbInfo {
        private readonly IEmulatedClock _clock;

        public SbInfo(IEmulatedClock clock) {
            _clock = clock;
            _dac = new DacState(this, _clock);
        }

        public uint FreqHz { get; set; }

        public class DmaState {
            public bool Stereo { get; set; }
            public bool Sign { get; set; }
            public bool AutoInit { get; set; }
            public bool FirstTransfer { get; set; } = true;
            public DmaMode Mode { get; set; }
            public uint Rate { get; set; }        // sample rate
            public uint Mul { get; set; }         // samples-per-millisecond multiplier
            public uint SingleSize { get; set; }  // size for single cycle transfers
            public uint AutoSize { get; set; }    // size for auto init transfers
            public uint Left { get; set; }        // Left in active cycle
            public uint Min { get; set; }
            public byte[] Buf8 { get; } = new byte[DmaBufSize];
            public short[] Buf16 { get; } = new short[DmaBufSize];
            public uint Bits { get; set; }
            public DmaChannel? Channel { get; set; }
            public uint RemainSize { get; set; }
        }
        public DmaState Dma { get; } = new();

        public bool SpeakerEnabled { get; set; }
        public bool MidiEnabled { get; set; }

        public byte TimeConstant { get; set; }

        public DspMode Mode { get; set; }
        public SbType Type { get; set; }

        // ESS chipset emulation, to be set only for SbType::SBPro2
        public EssType EssType { get; set; }

        public class IrqState {
            public bool Pending8Bit { get; set; }
            public bool Pending16Bit { get; set; }
        }
        public IrqState Irq { get; } = new();

        public class DspStateInfo {
            public DspState State { get; set; }
            public byte Cmd { get; set; }
            public byte CmdLen { get; set; }

            public class FifoState {
                // Last values added to the fifo
                public byte LastVal { get; set; }
                public byte[] Data { get; } = new byte[DspBufSize];

                // Index of current entry
                public byte Pos { get; set; }

                // Number of entries in the fifo
                public byte Used { get; set; }
            }
            public FifoState In { get; } = new();
            public FifoState Out { get; } = new();

            public byte TestRegister { get; set; }
            public byte WriteStatusCounter { get; set; }

            public uint ResetTally { get; set; }

            public int ColdWarmupMs { get; set; }
            public int HotWarmupMs { get; set; }
            public int WarmupRemainingMs { get; set; }
        }
        public DspStateInfo Dsp { get; } = new();

        public class DacState {
            // We use two criteria to monitor and decide when the rate's changed:
            // percent difference (versus current) and when the new rate persists
            // across a sequential count. These two thresholds (1% change confirmed
            // across 10 sequential changes) were selected based on inspecting
            // actual games and demos (Alone in the Dark, Overload demo, EMF demo,
            // Cronolog demo, Chickens demo). These get the timing right without
            // whip-sawing or chasing.
            private const float PercentDifferenceThreshold = 0.01f;
            private const int SequentialChangesThreshold = 10;
            private const float MillisInSecond = 1000.0f;

            private readonly SbInfo _sb;
            private readonly IEmulatedClock _clock;

            private double _lastWriteMs;
            private int _currentRateHz = MinPlaybackRateHz;
            private int _sequentialChangesTally;

            public DacState(SbInfo sb, IEmulatedClock clock) {
                _sb = sb;
                _clock = clock;
            }

            public AudioFrame RenderFrame() {
                if (_sb.SpeakerEnabled && _sb.Dsp.In.Data.Length > 0) {
                    float sample = LookupTables.ToUnsigned8(_sb.Dsp.In.Data[0]);
                    return new AudioFrame(sample, sample);
                }
                return new AudioFrame(0.0f, 0.0f);
            }

            // When the DAC is in use, we run the Sound Blaster at exactly the rate
            // the DAC is being written to and generate frame by frame. To support
            // this, we need to measure the rate the DAC is being written to.
            public int? MeasureDacRateHz() {
                double currWriteMs = _clock.ElapsedTimeMs;
                double elapsedMs = currWriteMs - _lastWriteMs;
                _lastWriteMs = currWriteMs;

                if (elapsedMs <= 0) {
                    return null;
                }

                double measuredRate = MillisInSecond / elapsedMs;

                double changePct = Math.Abs(measuredRate - _currentRateHz) / _currentRateHz;

                _sequentialChangesTally = (changePct > PercentDifferenceThreshold)
                    ? _sequentialChangesTally + 1
                    : 0;

                if (_sequentialChangesTally > SequentialChangesThreshold) {
                    _sequentialChangesTally = 0;
                    _currentRateHz = (int)Math.Round(measuredRate);
                    return _currentRateHz;
                }

                return null;
            }
        }

        private DacState _dac;
        public DacState Dac {
            get => _dac;
            set => _dac = value;
        }

        public class MixerState {
            public byte Index { get; set; } = 0;

            // If true, DOS software can programmatically change the Sound
            // Blaster mixer's volume levels on SB Pro 1 and later card for
            // the SB (DAC), OPL (FM) and CDAUDIO channels. These are called
            // "app levels". The final output level is the "user level" set
            // in the DOSBox mixer multiplied with the "app level" for these
            // channels.
            //
            // If the Sound Blaster mixer is disabled, the "app levels"
            // default to unity gain.
            public bool Enabled { get; set; } = false;

            public byte[] Dac { get; } = new byte[2];  // can be controlled programmatically
            public byte[] Fm { get; } = new byte[2];   // can be controlled programmatically
            public byte[] Cda { get; } = new byte[2];  // can be controlled programmatically

            public byte[] Master { get; } = new byte[2];
            public byte[] Lin { get; } = new byte[2];
            public byte Mic { get; set; } = 0;

            public bool StereoEnabled { get; set; } = false;

            public bool FilterEnabled { get; set; } = true;
            public bool FilterConfigured { get; set; } = false;
            public bool FilterAlwaysOn { get; set; } = false;

            public byte[] Unhandled { get; } = new byte[0x48];

            public byte[] EssIdStr { get; } = new byte[4];
            public byte EssIdStrPos { get; set; } = 0;
        }

        public MixerState Mixer { get; } = new MixerState();

        public class AdpcmState {
            public byte Reference { get; set; } = 0;
            public ushort Stepsize { get; set; } = 0;
            public bool HaveRef { get; set; } = false;
        }

        public AdpcmState Adpcm { get; set; } = new AdpcmState();

        public class HwState {
            public ushort Base { get; set; } = 0;
            public byte Irq { get; set; } = 0;
            public byte Dma8 { get; set; } = 0;
            public byte Dma16 { get; set; } = 0;
        }

        public HwState Hw { get; } = new HwState();

        public class E2State {
            public int Value { get; set; } = 0;
            public uint Count { get; set; } = 0;
        }

        public E2State E2 { get; } = new E2State();
    }

    // Number of bytes in input for commands (sb/sbpro)
    private static readonly byte[] DspCommandLengthsSb = [
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0x00
        1, 0, 0, 0,  2, 2, 2, 2,  0, 0, 0, 0,  0, 0, 0, 0,  // 0x10 Wari hack
        0, 0, 0, 0,  2, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0x20
        0, 0, 0, 0,  0, 0, 0, 0,  1, 0, 0, 0,  0, 0, 0, 0,  // 0x30
        1, 2, 2, 0,  0, 0, 0, 0,  2, 0, 0, 0,  0, 0, 0, 0,  // 0x40
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0x50
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0x60
        0, 0, 0, 0,  2, 2, 2, 2,  0, 0, 0, 0,  0, 0, 0, 0,  // 0x70
        2, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0x80
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0x90
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0xA0
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0xB0
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0xC0
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0xD0
        1, 0, 1, 0,  1, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0xE0
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0   // 0xF0
    ];

    // Number of bytes in input for commands (sb16)
    private static readonly byte[] DspCommandLengthsSb16 = [
        0, 0, 0, 0,  1, 2, 0, 0,  1, 0, 0, 0,  0, 0, 2, 1,  // 0x00
        1, 0, 0, 0,  2, 2, 2, 2,  0, 0, 0, 0,  0, 0, 0, 0,  // 0x10 Wari hack
        0, 0, 0, 0,  2, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0x20
        0, 0, 0, 0,  0, 0, 0, 0,  1, 0, 0, 0,  0, 0, 0, 0,  // 0x30
        1, 2, 2, 0,  0, 0, 0, 0,  2, 0, 0, 0,  0, 0, 0, 0,  // 0x40
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0x50
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0x60
        0, 0, 0, 0,  2, 2, 2, 2,  0, 0, 0, 0,  0, 0, 0, 0,  // 0x70
        2, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0x80
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0x90
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0xA0
        3, 3, 3, 3,  3, 3, 3, 3,  3, 3, 3, 3,  3, 3, 3, 3,  // 0xB0
        3, 3, 3, 3,  3, 3, 3, 3,  3, 3, 3, 3,  3, 3, 3, 3,  // 0xC0
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0xD0
        1, 0, 1, 0,  1, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0xE0
        0, 0, 0, 0,  0, 0, 0, 0,  0, 1, 0, 0,  0, 0, 0, 0   // 0xF0
    ];

    private static readonly int[][] E2IncrTable = [
        [0x01, -0x02, -0x04,  0x08, -0x10,  0x20,  0x40, -0x80, -106],
        [-0x01,  0x02, -0x04,  0x08,  0x10, -0x20,  0x40, -0x80,  165],
        [-0x01,  0x02,  0x04, -0x08,  0x10, -0x20, -0x40,  0x80, -151],
        [0x01, -0x02,  0x04, -0x08, -0x10,  0x20, -0x40,  0x80,   90]
    ];

    private readonly SbInfo _sb;
    private readonly SoundBlasterHardwareConfig _config;
    private readonly DualPic _dualPic;
    private readonly SoftwareMixer _mixer;
    private readonly SoundChannel _dacChannel;
    private readonly Opl3Fm _opl;
    private readonly DeviceScheduler _scheduler;
    private readonly IEmulatedClock _clock;
    private readonly DmaBus _dmaBus;
    private readonly RWQueue<AudioFrame> _outputQueue = new(4096);
    private int _framesNeeded = 0;

    /// <inheritdoc />
    public RWQueue<AudioFrame> OutputQueue => _outputQueue;

    /// <inheritdoc />
    public SoundChannel Channel => _dacChannel;

    private readonly AudioFrame[] _enqueueBatch = new AudioFrame[4096];
    private int _enqueueBatchCount;

    private AudioFrame[] _emptyFrames = Array.Empty<AudioFrame>();

    private readonly byte[] _aspRegs = new byte[256];
    private bool _aspInitInProgress;

    private double _lastDmaCallbackTime;

    private float _frameCounter = 0.0f;

    private int _framesAddedThisTick = 0;

    private TimingType _timingType = TimingType.None;

    private readonly EventHandler _perTickHandler;
    private readonly EventHandler _perFrameHandler;

    private readonly Func<ReadOnlySpan<byte>, int, float> _toFloatUnsigned8;
    private readonly Func<ReadOnlySpan<sbyte>, int, float> _toFloatSigned8;
    private readonly Func<ReadOnlySpan<ushort>, int, float> _toFloatUnsigned16;
    private readonly Func<ReadOnlySpan<short>, int, float> _toFloatSigned16;

    private const int MaxSingleFrameBaseCount = sizeof(short) * 2 - 1;
}
