namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

using Serilog.Events;

using Spice86.Audio.Backend;
using Spice86.Audio.Common;
using Spice86.Audio.Filters;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.EmulationLoopScheduler;

using System;
using System.Collections.Generic;

public partial class SoundBlaster {
    private const int DmaBufSize = 1024;
    private const int DspBufSize = 64;
    private const int MinPlaybackRateHz = 5000;
    private const ushort DefaultPlaybackRateHz = 22050;
    private const int NativeDacRateHz = 45454;
    private const int SbShift = 14;
    private const ushort SbShiftMask = (1 << SbShift) - 1;
    private const byte MinAdaptiveStepSize = 0;
    private const byte DspInitialResetLimit = 4;

    private enum DspState { Reset, ResetWait, Normal, HighSpeed }
    private enum DmaMode { None, Adpcm2Bit, Adpcm3Bit, Adpcm4Bit, Pcm8Bit, Pcm16Bit, Pcm16BitAliased }
    private enum DspMode { None, Dac, Dma, DmaPause, DmaMasked }
    private enum EssType { None, Es1688 }
    private enum SbIrq { Irq8, Irq16, IrqMpu }
    private enum BlasterState { WaitingForCommand, ResetRequest, Resetting, ReadingCommand }
    private enum TimingType { None, PerTick, PerFrame }

    /// <summary>
    /// Sound Blaster mixer register indices.
    /// </summary>
    private enum MixerRegister : byte {
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
    }

    private class SbInfo {
        public uint FreqHz { get; set; }

        public class DmaState {
            public bool Stereo { get; set; }
            public bool Sign { get; set; }
            public bool AutoInit { get; set; }
            public bool FirstTransfer { get; set; } = true;
            public DmaMode Mode { get; set; }
            public uint Rate { get; set; }
            public uint Mul { get; set; }
            public uint SingleSize { get; set; }
            public uint AutoSize { get; set; }
            public uint Left { get; set; }
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
                public byte LastVal { get; set; }
                public byte[] Data { get; } = new byte[DspBufSize];
                public byte Pos { get; set; }
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

        public class MixerState {
            public byte Index { get; set; } = 0;
            public bool Enabled { get; set; } = false;
            public byte[] Dac { get; } = new byte[2];
            public byte[] Fm { get; } = new byte[2];
            public byte[] Cda { get; } = new byte[2];
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

        public class DacState {
            private readonly SbInfo _sb;

            public DacState(SbInfo sb) {
                _sb = sb;
            }

            public AudioFrame RenderFrame() {
                if (_sb.SpeakerEnabled && _sb.Dsp.In.Data.Length > 0) {
                    float sample = LookupTables.ToUnsigned8(_sb.Dsp.In.Data[0]);
                    return new AudioFrame(sample, sample);
                }
                return new AudioFrame(0.0f, 0.0f);
            }
        }

        private DacState? _dac;

        public DacState Dac => _dac ??= new DacState(this);
    }

    private static readonly byte[] DspCommandLengthsSb = new byte[256] {
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        1, 0, 0, 0,  2, 2, 2, 2,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  2, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  0, 0, 0, 0,  1, 0, 0, 0,  0, 0, 0, 0,
        1, 2, 2, 0,  0, 0, 0, 0,  2, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  2, 2, 2, 2,  0, 0, 0, 0,  0, 0, 0, 0,
        2, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        1, 0, 1, 0,  1, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0
    };

    private static readonly byte[] DspCommandLengthsSb16 = new byte[256] {
        0, 0, 0, 0,  1, 2, 0, 0,  1, 0, 0, 0,  0, 0, 2, 1,
        1, 0, 0, 0,  2, 2, 2, 2,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  2, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  0, 0, 0, 0,  1, 0, 0, 0,  0, 0, 0, 0,
        1, 2, 2, 0,  0, 0, 0, 0,  2, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  2, 2, 2, 2,  0, 0, 0, 0,  0, 0, 0, 0,
        2, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        3, 3, 3, 3,  3, 3, 3, 3,  3, 3, 3, 3,  3, 3, 3, 3,
        3, 3, 3, 3,  3, 3, 3, 3,  3, 3, 3, 3,  3, 3, 3, 3,
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        1, 0, 1, 0,  1, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  0, 0, 0, 0,  0, 1, 0, 0,  0, 0, 0, 0
    };

    private static readonly int[][] E2IncrTable = new int[][] {
        new int[] { 0x01, -0x02, -0x04, 0x08, -0x10, 0x20, 0x40, -0x80, -106 },
        new int[] { -0x01, 0x02, -0x04, 0x08, 0x10, -0x20, 0x40, -0x80, 165 },
        new int[] { -0x01, 0x02, 0x04, -0x08, 0x10, -0x20, -0x40, 0x80, -151 },
        new int[] { 0x01, -0x02, 0x04, -0x08, -0x10, 0x20, -0x40, 0x80, 90 }
    };

    private readonly SbInfo _sb = new();
    private readonly SoundBlasterHardwareConfig _config;
    private readonly DualPic _dualPic;
    private readonly DmaChannel _primaryDmaChannel;
    private readonly DmaChannel? _secondaryDmaChannel;
    private readonly SoftwareMixer _mixer;
    private readonly SoundChannel _dacChannel;
    private readonly Opl3Fm _opl;
    private readonly EmulationLoopScheduler _scheduler;
    private readonly IEmulatedClock _clock;
    private readonly RWQueue<AudioFrame> _outputQueue;
    private readonly AudioFrame[] _enqueueBatch = new AudioFrame[4096];
    private int _enqueueBatchCount;
    private readonly AudioFrame[] _dequeueBatch = new AudioFrame[4096];
    private Queue<byte> _outputData = new Queue<byte>();
    private BlasterState _blasterState = BlasterState.WaitingForCommand;
    private double _lastDmaCallbackTime;
    private float _frameCounter = 0.0f;
    private int _framesNeeded = 0;
    private int _framesAddedThisTick = 0;
    private TimingType _timingType = TimingType.None;
    private const int MaxSingleFrameBaseCount = sizeof(short) * 2 - 1;
}