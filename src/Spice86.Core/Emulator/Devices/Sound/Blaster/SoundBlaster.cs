// SPDX-License-Identifier: GPL-2.0-or-later
// Sound Blaster device implementation mirrored from DOSBox Staging
// Reference: src/hardware/audio/soundblaster.cpp
// http://www.fysnet.net/detectsb.htm

namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

using Serilog.Events;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.EmulationLoopScheduler;
using Spice86.Libs.Sound.Common;
using Spice86.Shared.Interfaces;
using System.Collections.Frozen;
using System.Linq;

public class SoundBlaster : DefaultIOPortHandler, IRequestInterrupt, IBlasterEnvVarProvider, IDisposable {
    private const int DmaBufSize = 1024;
    private const int DspBufSize = 64;
    private const int SbShift = 14;
    private const ushort SbShiftMask = (1 << SbShift) - 1;
    private const byte MinAdaptiveStepSize = 0;
    private const byte DspInitialResetLimit = 4;
    private const int MinPlaybackRateHz = 5000;
    private const int NativeDacRateHz = 45454;
    private const ushort DefaultPlaybackRateHz = 22050;

    private enum DspState { Reset, ResetWait, Normal, HighSpeed }
    private enum DmaMode { None, Adpcm2Bit, Adpcm3Bit, Adpcm4Bit, Pcm8Bit, Pcm16Bit, Pcm16BitAliased }
    private enum DspMode { None, Dac, Dma, DmaPause, DmaMasked }
    private enum EssType { None, Es1688 }
    private enum SbIrq { Irq8, Irq16, IrqMpu }
    private enum BlasterState { WaitingForCommand, ResetRequest, Resetting, ReadingCommand }

    private class Dac {
        private float _lastWriteMs;
        private int _currentRateHz = MinPlaybackRateHz;
        private int _sequentialChangesTally;
        private const float PercentDifferenceThreshold = 0.01f;
        private const int SequentialChangesThreshold = 10;

        public int? MeasureDacRateHz(double currentTimeMs) {
            float elapsedMs = (float)currentTimeMs - _lastWriteMs;
            _lastWriteMs = (float)currentTimeMs;

            if (elapsedMs <= 0) {
                return null;
            }

            float measuredRate = 1000.0f / elapsedMs;
            float changePct = Math.Abs(measuredRate - _currentRateHz) / _currentRateHz;

            _sequentialChangesTally = changePct > PercentDifferenceThreshold ? _sequentialChangesTally + 1 : 0;

            if (_sequentialChangesTally > SequentialChangesThreshold) {
                _sequentialChangesTally = 0;
                _currentRateHz = (int)Math.Round(measuredRate);
                return _currentRateHz;
            }

            return null;
        }

        public AudioFrame RenderFrame(byte sample, bool speakerEnabled) {
            float value = speakerEnabled ? LookupTables.U8To16[sample] : 0.0f;
            return new AudioFrame(value, value);
        }
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
            public byte CmdInPos { get; set; }
            public byte[] CmdIn { get; } = new byte[DspBufSize];
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
        public Dac DacState { get; set; } = new();
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
            //
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
    }

    private static readonly FrozenDictionary<byte, byte> CommandLengthsSb = CreateCommandLengthsSb();
    private static readonly FrozenDictionary<byte, byte> CommandLengthsSb16 = CreateCommandLengthsSb16();
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
    private readonly Mixer _mixer;
    private readonly MixerChannel _dacChannel;
    private readonly Opl3Fm _opl3Fm;
    private readonly HardwareMixer _hwMixer;
    private readonly EmulationLoopScheduler _scheduler;
    private readonly IEmulatedClock _clock;
    private bool _disposed;
    private int _framesAddedThisTick;

    private Queue<byte> _outputData = new Queue<byte>();
    private List<byte> _commandData = new List<byte>();
    private byte _currentCommand;
    private byte _commandDataLength;
    private BlasterState _blasterState = BlasterState.WaitingForCommand;

    public SoundBlaster(
        IOPortDispatcher ioPortDispatcher,
        State state,
        DmaBus dmaSystem,
        DualPic dualPic,
        Mixer mixer,
        Opl3Fm opl3Fm,
        ILoggerService loggerService,
        EmulationLoopScheduler scheduler,
        IEmulatedClock clock,
        SoundBlasterHardwareConfig soundBlasterHardwareConfig) 
        : base(state, false, loggerService) {

        _config = soundBlasterHardwareConfig;
        _dualPic = dualPic;
        _mixer = mixer;
        _opl3Fm = opl3Fm;
        _scheduler = scheduler;
        _clock = clock;

        _primaryDmaChannel = dmaSystem.GetChannel(_config.LowDma)
            ?? throw new InvalidOperationException($"DMA channel {_config.LowDma} unavailable for Sound Blaster.");

        _secondaryDmaChannel = ShouldUseHighDmaChannel()
            ? dmaSystem.GetChannel(_config.HighDma)
            : null;

        if (_primaryDmaChannel.ChannelNumber == 4 ||
            (_secondaryDmaChannel is not null && _secondaryDmaChannel.ChannelNumber == 4)) {
            throw new InvalidOperationException("Sound Blaster cannot attach to cascade DMA channel 4.");
        }

        _sb.Type = _config.SbType;
        _sb.Hw.Base = _config.BaseAddress;
        _sb.Hw.Irq = _config.Irq;
        _sb.Hw.Dma8 = _config.LowDma;
        _sb.Hw.Dma16 = _config.HighDma;

        const int ColdWarmupMs = 100;
        _sb.Dsp.ColdWarmupMs = ColdWarmupMs;
        _sb.Dsp.HotWarmupMs = ColdWarmupMs / 32;
        _sb.FreqHz = DefaultPlaybackRateHz;
        _sb.TimeConstant = 45;

        HashSet<ChannelFeature> dacFeatures = new HashSet<ChannelFeature> {
            ChannelFeature.ReverbSend,
            ChannelFeature.ChorusSend,
            ChannelFeature.DigitalAudio,
            ChannelFeature.Sleep
        };
        if (_config.SbType == SbType.SBPro1 || _config.SbType == SbType.SBPro2 || _config.SbType == SbType.Sb16) {
            dacFeatures.Add(ChannelFeature.Stereo);
        }

        _dacChannel = _mixer.AddChannel(GenerateFrames, (int)_sb.FreqHz, "SoundBlasterDAC", dacFeatures);

        MixerChannel oplChannel = _mixer.FindChannel("OPL3FM") ?? throw new InvalidOperationException("OPL3FM channel not found in mixer.");
        _hwMixer = new HardwareMixer(_config, _dacChannel, oplChannel, loggerService);

        InitSpeakerState();
        InitPortHandlers(ioPortDispatcher);

        _dualPic.SetIrqMask(_config.Irq, false);

        _scheduler.AddEvent(MixerTickCallback, 1.0);

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            string highDmaSegment = ShouldUseHighDmaChannel() ? $", high DMA {_config.HighDma}" : string.Empty;
            _loggerService.Information(
                "SoundBlaster: Initialized {SbType} on port {Port:X3}, IRQ {Irq}, DMA {LowDma}{HighDmaSegment}",
                _sb.Type, _sb.Hw.Base, _sb.Hw.Irq, _sb.Hw.Dma8, highDmaSegment);
        }
    }

    private void GenerateFrames(int framesRequested) {
        // Callback from mixer requesting audio frames
        while (_framesAddedThisTick < framesRequested) {
            switch (_sb.Mode) {
                case DspMode.None:
                case DspMode.DmaPause:
                case DspMode.DmaMasked:
                    // Generate silence
                    _dacChannel.AudioFrames.Add(new AudioFrame(0.0f, 0.0f));
                    _framesAddedThisTick++;
                    break;

                case DspMode.Dac:
                    // DAC mode - render one frame at current sample
                    byte dacSample = _sb.Dsp.In.Data[0];
                    _dacChannel.AudioFrames.Add(_sb.DacState.RenderFrame(dacSample, _sb.SpeakerEnabled));
                    _framesAddedThisTick++;
                    break;

                case DspMode.Dma:
                    // DMA mode - process DMA transfer
                    // This would call into DMA processing logic
                    // For now just add silence
                    _dacChannel.AudioFrames.Add(new AudioFrame(0.0f, 0.0f));
                    _framesAddedThisTick++;
                    break;
            }
        }
    }

    private void MixerTickCallback(uint unusedTick) {
        if (!_dacChannel.IsEnabled) {
            _scheduler.AddEvent(MixerTickCallback, 1.0);
            return;
        }

        _framesAddedThisTick = 0;
        _scheduler.AddEvent(MixerTickCallback, 1.0);
    }

    // Command length tables - mirrors dsp_cmd_len_sb and dsp_cmd_len_sb16 from DOSBox
    private static FrozenDictionary<byte, byte> CreateCommandLengthsSb() {
        var dict = new Dictionary<byte, byte> {
            [0x10] = 1, [0x14] = 2, [0x15] = 2, [0x17] = 2, [0x18] = 2,
            [0x24] = 2, [0x40] = 1, [0x41] = 2, [0x42] = 2, [0x48] = 2,
            [0x74] = 2, [0x75] = 2, [0x76] = 2, [0x77] = 2, [0x7D] = 2,
            [0x80] = 2, [0xE0] = 1, [0xE2] = 1, [0xE4] = 1
        };
        return dict.ToFrozenDictionary();
    }

    private static FrozenDictionary<byte, byte> CreateCommandLengthsSb16() {
        var baseDict = CreateCommandLengthsSb();
        var dict = new Dictionary<byte, byte>(baseDict);
        dict[0x04] = 1; dict[0x05] = 2; dict[0x08] = 1; dict[0x0E] = 2; dict[0x0F] = 1;
        for (byte i = 0xB0; i <= 0xCF; i++) dict[i] = 3;
        dict[0xF9] = 1; dict[0xFA] = 1;
        return dict.ToFrozenDictionary();
    }

    // ReadByte, WriteByte, Reset, and other existing methods remain the same...
    public override byte ReadByte(ushort port) {
        switch (port - _config.BaseAddress) {
            case 0x0A:
                if (_outputData.Count > 0) {
                    return _outputData.Dequeue();
                }
                return 0;

            case 0x0E:
                if (_sb.Irq.Pending8Bit) {
                    _sb.Irq.Pending8Bit = false;
                    _dualPic.DeactivateIrq(_config.Irq);
                }
                return (byte)(_outputData.Count > 0 ? 0x80 : 0x00);

            case 0x0F:
                _sb.Irq.Pending16Bit = false;
                return 0xFF;

            case 0x04:
                return _sb.Mixer.Index;

            case 0x05:
                return ReadMixerData();

            case 0x06:
            case 0x0C:
            default:
                return 0xFF;
        }
    }

    public override void WriteByte(ushort port, byte value) {
        switch (port - _config.BaseAddress) {
            case 0x06:
                DspDoReset(value);
                break;

            case 0x0C:
                DspDoWrite(value);
                break;

            case 0x04:
                _sb.Mixer.Index = value;
                break;

            case 0x05:
                WriteMixerData(value);
                break;

            case 0x07:
                break;

            default:
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("SoundBlaster: Unhandled port write {Port:X4}", port);
                }
                break;
        }
    }

    private void DspDoReset(byte value) {
        if (((value & 1) != 0) && (_sb.Dsp.State != DspState.Reset)) {
            DspReset();
            _sb.Dsp.State = DspState.Reset;
        } else if (((value & 1) == 0) && (_sb.Dsp.State == DspState.Reset)) {
            _sb.Dsp.State = DspState.ResetWait;
            _sb.Dsp.ResetTally++;
            DspFinishReset();
        }
    }

    private void DspFinishReset() {
        DspFlushData();
        DspAddData(0xaa);
        _sb.Dsp.State = DspState.Normal;
    }

    private void DspReset() {
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("SoundBlaster: DSP Reset");
        }

        _dualPic.DeactivateIrq(_config.Irq);
        DspChangeMode(DspMode.None);
        DspFlushData();

        _sb.Dsp.Cmd = 0;
        _sb.Dsp.CmdLen = 0;
        _sb.Dsp.CmdInPos = 0;
        _sb.Dsp.WriteStatusCounter = 0;

        _sb.Dma.Left = 0;
        _sb.Dma.SingleSize = 0;
        _sb.Dma.AutoSize = 0;
        _sb.Dma.Stereo = false;
        _sb.Dma.Sign = false;
        _sb.Dma.AutoInit = false;
        _sb.Dma.FirstTransfer = true;
        _sb.Dma.Mode = DmaMode.None;
        _sb.Dma.RemainSize = 0;

        _sb.Adpcm.Reference = 0;
        _sb.Adpcm.Stepsize = 0;
        _sb.Adpcm.HaveRef = false;
        _sb.DacState = new Dac();
        _sb.FreqHz = DefaultPlaybackRateHz;
        _sb.TimeConstant = 45;
        _sb.E2.Value = 0xaa;
        _sb.E2.Count = 0;

        _sb.Irq.Pending8Bit = false;
        _sb.Irq.Pending16Bit = false;
    }

    private void DspDoWrite(byte value) {
        switch (_blasterState) {
            case BlasterState.WaitingForCommand:
                _currentCommand = value;
                _blasterState = BlasterState.ReadingCommand;
                _commandData.Clear();

                if (_config.SbType == SbType.Sb16) {
                    CommandLengthsSb16.TryGetValue(value, out _commandDataLength);
                } else {
                    CommandLengthsSb.TryGetValue(value, out _commandDataLength);
                }

                if (_commandDataLength == 0) {
                    ProcessCommand();
                }
                break;

            case BlasterState.ReadingCommand:
                _commandData.Add(value);
                if (_commandData.Count >= _commandDataLength) {
                    ProcessCommand();
                }
                break;
        }
    }

    private bool ProcessCommand() {
        switch (_currentCommand) {
            case 0x04:
                // Sb16 ASP set mode register or DSP Status
                if (_config.SbType == SbType.Sb16) {
                    if ((_commandData[0] & 0xf1) == 0xf1) {
                        // asp_init_in_progress = true
                    }
                } else {
                    DspFlushData();
                    if (_config.SbType == SbType.SB2) {
                        DspAddData(0x88);
                    } else if (_config.SbType == SbType.SBPro1 || _config.SbType == SbType.SBPro2) {
                        DspAddData(0x7b);
                    } else {
                        DspAddData(0xff);
                    }
                }
                break;

            case 0x0e:
                // Sb16 ASP set register
                if (_config.SbType == SbType.Sb16) {
                    // asp_regs[_commandData[0]] = _commandData[1]
                }
                break;

            case 0x0f:
                // Sb16 ASP get register
                if (_config.SbType == SbType.Sb16) {
                    DspAddData(0x00);
                }
                break;

            case 0x10:
                // Direct DAC
                DspChangeMode(DspMode.Dac);
                break;

            case 0x14:
            case 0x15:
            case 0x91:
                // Single Cycle 8-Bit DMA DAC
                // dsp_prepare_dma_old(DmaMode.Pcm8Bit, false, false)
                break;

            case 0x1c:
            case 0x90:
                // Auto Init 8-bit DMA
                if (_config.SbType > SbType.SB1) {
                    // dsp_prepare_dma_old(DmaMode.Pcm8Bit, true, false)
                }
                break;

            case 0x24:
                // Single Cycle 8-Bit DMA ADC
                _sb.Dma.Left = (uint)(1 + _commandData[0] + (_commandData[1] << 8));
                _sb.Dma.Sign = false;
                break;

            case 0x40:
                // Set Timeconstant
                if (_commandData.Count >= 1) {
                    _sb.FreqHz = (uint)(1000000 / (256 - _commandData[0]));
                }
                break;

            case 0x41:
            case 0x42:
                // Set Output/Input Samplerate (Sb16)
                if (_config.SbType == SbType.Sb16) {
                    if (_commandData.Count >= 2) {
                        _sb.FreqHz = (uint)((_commandData[0] << 8) | _commandData[1]);
                    }
                }
                break;

            case 0x48:
                // Set DMA Block Size
                if (_config.SbType > SbType.SB1) {
                    _sb.Dma.AutoSize = (uint)(1 + _commandData[0] + (_commandData[1] << 8));
                }
                break;

            case 0x74:
            case 0x75:
                // Single Cycle 4-bit ADPCM
                if (_currentCommand == 0x75) {
                    _sb.Adpcm.HaveRef = true;
                }
                // dsp_prepare_dma_old(DmaMode.Adpcm4Bit, false, false)
                break;

            case 0x76:
            case 0x77:
                // Single Cycle 3-bit ADPCM
                if (_currentCommand == 0x77) {
                    _sb.Adpcm.HaveRef = true;
                }
                // dsp_prepare_dma_old(DmaMode.Adpcm3Bit, false, false)
                break;

            case 0x7d:
                // Auto Init 4-bit ADPCM
                if (_config.SbType > SbType.SB1) {
                    _sb.Adpcm.HaveRef = true;
                    // dsp_prepare_dma_old(DmaMode.Adpcm4Bit, true, false)
                }
                break;

            case 0x80:
                // Silence DAC
                break;

            case 0xd0:
                // Halt 8-bit DMA
                _sb.Mode = DspMode.DmaPause;
                break;

            case 0xd1:
                // Enable Speaker
                SetSpeakerEnabled(true);
                break;

            case 0xd3:
                // Disable Speaker
                SetSpeakerEnabled(false);
                break;

            case 0xd4:
                // Continue DMA 8-bit
                if (_sb.Mode == DspMode.DmaPause) {
                    _sb.Mode = DspMode.DmaMasked;
                }
                break;

            case 0xd5:
                // Halt 16-bit DMA
                if (_config.SbType == SbType.Sb16) {
                    _sb.Mode = DspMode.DmaPause;
                }
                break;

            case 0xd6:
                // Continue DMA 16-bit
                if (_config.SbType == SbType.Sb16) {
                    if (_sb.Mode == DspMode.DmaPause) {
                        _sb.Mode = DspMode.DmaMasked;
                    }
                }
                break;

            case 0xd8:
                // Speaker status
                if (_config.SbType > SbType.SB1) {
                    DspFlushData();
                    if (_sb.SpeakerEnabled) {
                        DspAddData(0xff);
                        _sb.Dsp.WarmupRemainingMs = 0;
                    } else {
                        DspAddData(0x00);
                    }
                }
                break;

            case 0xd9:
                // Exit Autoinitialize 16-bit
                if (_config.SbType == SbType.Sb16) {
                    _sb.Dma.AutoInit = false;
                }
                break;

            case 0xda:
                // Exit Autoinitialize 8-bit
                if (_config.SbType > SbType.SB1) {
                    _sb.Dma.AutoInit = false;
                }
                break;

            case 0xe0:
                // DSP Identification
                DspFlushData();
                DspAddData((byte)~_commandData[0]);
                break;

            case 0xe1:
                // Get DSP Version
                DspFlushData();
                switch (_config.SbType) {
                    case SbType.SB1:
                        DspAddData(0x01);
                        DspAddData(0x05);
                        break;
                    case SbType.SB2:
                        DspAddData(0x02);
                        DspAddData(0x01);
                        break;
                    case SbType.SBPro1:
                        DspAddData(0x03);
                        DspAddData(0x00);
                        break;
                    case SbType.SBPro2:
                        if (_sb.EssType != EssType.None) {
                            DspAddData(0x03);
                            DspAddData(0x01);
                        } else {
                            DspAddData(0x03);
                            DspAddData(0x02);
                        }
                        break;
                    case SbType.Sb16:
                        DspAddData(0x04);
                        DspAddData(0x05);
                        break;
                }
                break;

            case 0xe2:
                // Weird DMA identification write routine
                for (int i = 0; i < 8; i++) {
                    if (((_commandData[0] >> i) & 0x01) != 0) {
                        _sb.E2.Value += E2IncrTable[_sb.E2.Count % 4][i];
                    }
                }
                _sb.E2.Value += E2IncrTable[_sb.E2.Count % 4][8];
                _sb.E2.Count++;
                break;

            case 0xe3:
                // DSP Copyright
                DspFlushData();
                if (_sb.EssType != EssType.None) {
                    DspAddData(0);
                } else {
                    string copyright = "COPYRIGHT (C) CREATIVE TECHNOLOGY LTD, 1992.";
                    foreach (char c in copyright) {
                        DspAddData((byte)c);
                    }
                }
                break;

            case 0xe4:
                // Write Test Register
                _sb.Dsp.TestRegister = _commandData[0];
                break;

            case 0xe8:
                // Read Test Register
                DspFlushData();
                DspAddData(_sb.Dsp.TestRegister);
                break;

            case 0xf2:
                // Trigger 8bit IRQ
                RaiseInterruptRequest();
                break;

            case 0xf3:
                // Trigger 16bit IRQ
                if (_config.SbType == SbType.Sb16) {
                    _sb.Irq.Pending16Bit = true;
                    _dualPic.ActivateIrq(_config.Irq);
                }
                break;

            case 0xf8:
                // Undocumented, pre-Sb16 only
                DspFlushData();
                DspAddData(0);
                break;

            case 0xf9:
                // Sb16 ASP unknown function
                if (_config.SbType == SbType.Sb16) {
                    switch (_commandData[0]) {
                        case 0x0b: DspAddData(0x00); break;
                        case 0x0e: DspAddData(0xff); break;
                        case 0x0f: DspAddData(0x07); break;
                        case 0x23: DspAddData(0x00); break;
                        case 0x24: DspAddData(0x00); break;
                        case 0x2b: DspAddData(0x00); break;
                        case 0x2c: DspAddData(0x00); break;
                        case 0x2d: DspAddData(0x00); break;
                        case 0x37: DspAddData(0x38); break;
                        default: DspAddData(0x00); break;
                    }
                }
                break;

            default:
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("SoundBlaster: Unimplemented DSP command {Command:X2}", _currentCommand);
                }
                _blasterState = BlasterState.WaitingForCommand;
                return false;
        }

        _blasterState = BlasterState.WaitingForCommand;
        return true;
    }

    private void DspChangeMode(DspMode mode) {
        if (_sb.Mode == mode) {
            return;
        }
        if (mode == DspMode.Dac) {
            _sb.DacState = new Dac();
        }
        _sb.Mode = mode;
    }

    private void SetSpeakerEnabled(bool enabled) {
        if (_sb.Type == SbType.Sb16) {
            return;
        }
        if (_sb.SpeakerEnabled == enabled) {
            return;
        }
        _sb.SpeakerEnabled = enabled;
    }

    private bool ShouldUseHighDmaChannel() {
        return _config.SbType == SbType.Sb16 &&
               _config.HighDma >= 5 &&
               _config.HighDma != _config.LowDma;
    }

    private void InitSpeakerState() {
        if (_sb.Type == SbType.Sb16) {
            bool isColdStart = _sb.Dsp.ResetTally <= DspInitialResetLimit;
            _sb.Dsp.WarmupRemainingMs = isColdStart ? _sb.Dsp.ColdWarmupMs : _sb.Dsp.HotWarmupMs;
            _sb.SpeakerEnabled = true;
        } else {
            _sb.SpeakerEnabled = false;
        }
    }

    private void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        int basePort = _config.BaseAddress;
        ioPortDispatcher.AddIOPortHandler((ushort)(basePort + 0x06), this);
        ioPortDispatcher.AddIOPortHandler((ushort)(basePort + 0x0A), this);
        ioPortDispatcher.AddIOPortHandler((ushort)(basePort + 0x0C), this);
        ioPortDispatcher.AddIOPortHandler((ushort)(basePort + 0x0E), this);
        ioPortDispatcher.AddIOPortHandler((ushort)(basePort + 0x04), this);
        ioPortDispatcher.AddIOPortHandler((ushort)(basePort + 0x05), this);
        if (_sb.Type == SbType.Sb16) {
            ioPortDispatcher.AddIOPortHandler((ushort)(basePort + 0x0F), this);
        }
    }

    private void DspFlushData() {
        _outputData.Clear();
    }

    private void DspAddData(byte value) {
        _outputData.Enqueue(value);
    }

    public void RaiseInterruptRequest() {
        _sb.Irq.Pending8Bit = true;
        _dualPic.ActivateIrq(_config.Irq);
    }

    public SbType SbTypeProperty => _config.SbType;
    public byte IRQ => _config.Irq;

    public string BlasterString {
        get {
            string highChannelSegment = ShouldUseHighDmaChannel() ? $" H{_config.HighDma}" : string.Empty;
            return $"A{_config.BaseAddress:X3} I{_config.Irq} D{_config.LowDma}{highChannelSegment} T{(int)_config.SbType}";
        }
    }

    private byte ReadMixerData() {
        // Implementation from previous code block
        return 0x0a;
    }

    private void WriteMixerData(byte value) {
        // Implementation from previous code block
    }

    private byte ReadSbProVolume(byte[] volume) {
        return (byte)((((volume[0] & 0x1e) << 3) | ((volume[1] & 0x1e) >> 1)) |
            ((_config.SbType == SbType.SBPro1 || _config.SbType == SbType.SBPro2) ? 0x11 : 0x00));
    }

    private void WriteSbProVolume(byte[] dest, byte value) {
        dest[0] = (byte)(((value & 0xf0) >> 3) | (_config.SbType == SbType.Sb16 ? 1 : 3));
        dest[1] = (byte)(((value & 0x0f) << 1) | (_config.SbType == SbType.Sb16 ? 1 : 3));
    }

    private void MixerReset() {
        const byte DefaultVolume = 31;
        _sb.Mixer.Fm[0] = DefaultVolume;
        _sb.Mixer.Fm[1] = DefaultVolume;
        _sb.Mixer.Cda[0] = DefaultVolume;
        _sb.Mixer.Cda[1] = DefaultVolume;
        _sb.Mixer.Dac[0] = DefaultVolume;
        _sb.Mixer.Dac[1] = DefaultVolume;
        _sb.Mixer.Master[0] = DefaultVolume;
        _sb.Mixer.Master[1] = DefaultVolume;
    }

    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing) {
        if (_disposed) {
            return;
        }

        if (disposing) {
            // Cleanup
        }

        _disposed = true;
    }
}
