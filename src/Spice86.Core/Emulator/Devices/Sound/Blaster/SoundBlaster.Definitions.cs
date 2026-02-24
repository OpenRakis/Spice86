namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

using Spice86.Audio.Backend;
using Spice86.Audio.Common;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.EmulationLoopScheduler;

using System;

using EventHandler = VM.EmulationLoopScheduler.EventHandler;

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

    private enum DspMode { None, Dac, Dma, DmaPause, DmaMasked }

    private enum EssType { None, Es1688 }

    private enum SbIrq { Irq8, Irq16, IrqMpu }

    private enum BlasterState : byte { WaitingForCommand, ReadingCommand }

    private enum TimingType { None, PerTick, PerFrame }

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

            private float _lastWriteMs;
            private int _currentRateHz = MinPlaybackRateHz;
            private int _sequentialChangesTally;

            public DacState(SbInfo sb, IEmulatedClock clock) {
                _sb = sb;
                _clock = clock;
            }

            public void Reset() {
                _lastWriteMs = 0;
                _currentRateHz = MinPlaybackRateHz;
                _sequentialChangesTally = 0;
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
                float currWriteMs = (float)_clock.ElapsedTimeMs;
                float elapsedMs = currWriteMs - _lastWriteMs;
                _lastWriteMs = currWriteMs;

                if (elapsedMs <= 0) {
                    return null;
                }

                float measuredRate = MillisInSecond / elapsedMs;

                float changePct = Math.Abs(measuredRate - _currentRateHz) / _currentRateHz;

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

        public AdpcmState Adpcm { get; } = new AdpcmState();

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
    private static readonly byte[] DspCommandLengthsSb = new byte[256] {
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
    };

    // Number of bytes in input for commands (sb16)
    private static readonly byte[] DspCommandLengthsSb16 = new byte[256] {
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
    };

    private static readonly int[][] E2IncrTable = new int[][] {
        new int[] {  0x01, -0x02, -0x04,  0x08, -0x10,  0x20,  0x40, -0x80, -106 },
        new int[] { -0x01,  0x02, -0x04,  0x08,  0x10, -0x20,  0x40, -0x80,  165 },
        new int[] { -0x01,  0x02,  0x04, -0x08,  0x10, -0x20, -0x40,  0x80, -151 },
        new int[] {  0x01, -0x02,  0x04, -0x08, -0x10,  0x20, -0x40,  0x80,   90 }
    };

    private static readonly byte[] AspRegs = new byte[256];

    private readonly SbInfo _sb;
    private readonly SoundBlasterHardwareConfig _config;
    private readonly DualPic _dualPic;
    private readonly DmaChannel _primaryDmaChannel;
    private readonly DmaChannel? _secondaryDmaChannel;
    private readonly SoftwareMixer _mixer;
    private readonly SoundChannel _dacChannel;
    private readonly Opl3Fm _opl;
    private readonly EmulationLoopScheduler _scheduler;
    private readonly IEmulatedClock _clock;
    private readonly RWQueue<AudioFrame> _outputQueue = new(4096);
    private int _framesNeeded = 0;

    /// <inheritdoc />
    public RWQueue<AudioFrame> OutputQueue => _outputQueue;

    /// <inheritdoc />
    public SoundChannel Channel => _dacChannel;

    private readonly AudioFrame[] _enqueueBatch = new AudioFrame[4096];
    private int _enqueueBatchCount;

    private readonly byte[] _aspRegs = new byte[256];
    private bool _aspInitInProgress;

    private double _lastDmaCallbackTime;

    private float _frameCounter = 0.0f;

    private int _framesAddedThisTick = 0;

    private TimingType _timingType = TimingType.None;

    private readonly EventHandler _perTickHandler;
    private readonly EventHandler _perFrameHandler;

    private const int MaxSingleFrameBaseCount = sizeof(short) * 2 - 1;
}
