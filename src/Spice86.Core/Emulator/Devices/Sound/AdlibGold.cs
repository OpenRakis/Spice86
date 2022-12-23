namespace Spice86.Core.Emulator.Devices.Sound;

using Dunet;

using Serilog;

using Spice86.Core.Backend.Audio;
using Spice86.Core.Backend.Audio.Iir;
using Spice86.Core.CLI;
using Spice86.Core.Emulator.Devices.Sound.Ym7128b;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Sound;
using Spice86.Core.Emulator.VM;

using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;

/// <summary>
/// Adlib Gold implementation, translated from DOSBox Staging code
/// </summary>
public sealed class AdlibGold : DefaultIOPortHandler, IDisposable  {
    private readonly StereoProcessor _stereoProcessor;
    private readonly SurroundProcessor _surroundProcessor;
    private readonly AudioPlayer? _audioPlayer;
    private readonly ushort _sampleRate = 0;

    private bool _endThread = false;

    private bool _initialized = false;

    private readonly bool _paused = false;

    private readonly Thread _playbackThread;

    public AdlibGold(Machine machine, Configuration configuration, ILoggerService loggerService, ushort sampleRate) : base(machine, configuration, loggerService) {
        _sampleRate = sampleRate;
        _stereoProcessor = new(_sampleRate, loggerService);
        _surroundProcessor = new(_sampleRate);
        _audioPlayer = Audio.CreatePlayer(48000, 2048);
        _playbackThread = new Thread(RnderWaveFormOnPlaybackThread);
    }

    private bool _disposed = false;


     public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing) {
        if(!_disposed) {
            if(disposing) {
                if (!_paused) {
                    _endThread = true;
                    if (_playbackThread.IsAlive) {
                        _playbackThread.Join();
                    }
                }
                _audioPlayer?.Dispose();
                _initialized = false;
            }
            _disposed = true;
        }
    }

    private enum StereoProcessorControlReg {
        VolumeLeft,
        VolumeRight,
        Bass,
        Treble,
        SwitchFunctions,
    }

    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(OPLConsts.FmMusicStatusPortNumber2, this);
        ioPortDispatcher.AddIOPortHandler(OPLConsts.FmMusicDataPortNumber2, this);
        ioPortDispatcher.AddIOPortHandler(0x332, this);
        ioPortDispatcher.AddIOPortHandler(0x333, this);
        ioPortDispatcher.AddIOPortHandler(0x38A, this);
    }

    public void StereoControlWrite(byte reg, byte data) => _stereoProcessor.ControlWrite((StereoProcessorControlReg)reg, data);

    public void SurroundControlWrite(byte data) => _surroundProcessor.ControlWrite(data);

    public override byte ReadByte(int port) {
        return base.ReadByte(port);
    }

    private void RnderWaveFormOnPlaybackThread() {
        if (_audioPlayer is null) {
            return;
        }
        const int length = 2;
        Span<float> buffer = stackalloc float[length];
        while (!_endThread) {
            // if (_fifo.TryDequeue(out AudioFrame frame)) {
            //     buffer[0] = frame.Left;
            //     buffer[1] = frame.Right;
            //     _audioPlayer?.WriteData(buffer);
            // } else {
                Thread.Sleep(1);
            // }
        }
    }

    public override void WriteByte(int port, byte value) {
        base.WriteByte(port, value);
    }

    public override void WriteWord(int port, ushort value) {
        base.WriteWord(port, value);
    }

    private void Process(short[] input, uint framesRemaining, ref AudioFrame output) {
        for (var index = 0; framesRemaining-- > 0; index++) {
            AudioFrame frame = new(input[0], input[1]);
            AudioFrame wet = _surroundProcessor.Process(ref frame);

            // Additionnal wet signal level boost to make the emulated
            // sound more closely resemble real hardware recordings.
            const float wetBoost = 1.8f;
            frame.Left = wet.Left * wetBoost;
            frame.Right = wet.Right * wetBoost;
            frame = _surroundProcessor.Process(ref frame);

            output[index] = frame.Left;
            output[index + 1] = frame.Right;
        }
    }

    [Union]
    private partial record StereoProcessorSwitchFunctions {
        public StereoProcessorSwitchFunctions(byte value) {
            Data = value;
            SourceSelector = value;
            StereoMode = value;
        }

        public StereoProcessorSwitchFunctions() { }

        public byte Data { get; set; }
        public byte SourceSelector { get; set; }
        public byte StereoMode { get; set; }
    }

    private enum StereoProcessorStereoMode {
        ForcedMono,
        LinearStereo,
        PseudoStereo,
        SpatialStereo
    }

    private enum StereoProcessorSourceSelector {
        SoundA1 = 2,
        SoundA2 = 3,
        SoundB1 = 4,
        SoundB2 = 5,
        Stereo1 = 6,
        Stereo2 = 7,
    }

    private struct AudioFrame {
        public AudioFrame(float left, float right) {
            Left = left;
            Right = right;
        }

        public float Left { get; set; }

        public float Right { get; set; }

        public float this[int i] {
            get { return int.IsEvenInteger(i) ? Left : Right; }
            set { if (int.IsEvenInteger(i)) { Left = value; } else { Right = value; } }
        }
    }

    /// <summary>
    /// Philips Semiconductors TDA8425 hi-fi stereo audio processor emulation
    /// </summary>
    private class StereoProcessor {
        private readonly ushort _sampleRate = 0;
        private AudioFrame _gain = new();
        private StereoProcessorSourceSelector _sourceSelector = new();
        private StereoProcessorStereoMode _stereoMode = new();

        // Stero low and high-shelf filters
        private readonly LowShelf[] _lowShelf = new LowShelf[] { new(), new() };
        private readonly HighShelf[] _highShelf = new HighShelf[] { new(), new() };
        readonly AllPass _allPass = new();

        private const int Volume0DbValue = 60;

        private const int ShelfFilter0DbValue = 6;

        private ILoggerService _loggerService;

        public StereoProcessor(ushort sampleRate, ILoggerService loggerService) {
            _loggerService = loggerService;
            _sampleRate = sampleRate;
            if (_sampleRate <= 0) {
                throw new IndexOutOfRangeException(nameof(_sampleRate));
            }

            const double AllPassFrequency = 400.0;
            const double QFactor = 1.7;
            _allPass.Setup(_sampleRate, AllPassFrequency, QFactor);
            Reset();
        }

        public void Reset() {
            ControlWrite(StereoProcessorControlReg.VolumeLeft, Volume0DbValue);
            ControlWrite(StereoProcessorControlReg.VolumeRight, Volume0DbValue);
            ControlWrite(StereoProcessorControlReg.Bass, ShelfFilter0DbValue);
            ControlWrite(StereoProcessorControlReg.Treble, ShelfFilter0DbValue);
            StereoProcessorSwitchFunctions sf = new() {
                SourceSelector = (byte)StereoProcessorSourceSelector.Stereo1,
                StereoMode = (byte)StereoProcessorStereoMode.LinearStereo
            };
            ControlWrite(StereoProcessorControlReg.SwitchFunctions, sf.Data);
        }

        public void ControlWrite(
            StereoProcessorControlReg reg,
            byte data) {
            float CalcVolumeGain(int value) {
                const float MinGainDb = -128.0f;
                const float MaxGainDb = 6.0f;
                const float StepDb = 2.0f;

                float val = (float)(value - Volume0DbValue);
                float gainDb = Math.Clamp(val * StepDb, MinGainDb, MaxGainDb);
                return MathUtils.DecibelToGain(gainDb);
            }

            float CalcFilterGainDb(int value) {
                const double MainGainDb = -12.0;
                const double MaxGainDb = 15.0;
                const double StepDb = 3.0;

                int val = value - ShelfFilter0DbValue;
                return (float)Math.Clamp(val * StepDb, MainGainDb, MaxGainDb);
            }

            const int VolumeControlWidth = 6;
            const int volumeControlMask = (1 << VolumeControlWidth) - 1;

            const int filter_control_width = 4;
            const int filterControlMask = (1 << filter_control_width) - 1;

            switch (reg) {
                case StereoProcessorControlReg.VolumeLeft: {
                        var value = data & volumeControlMask;
                        _gain.Left = CalcVolumeGain(value);
                        _loggerService.Debug("ADLIBGOLD: Stereo: Final left volume set to {Left}.2fdB {Value}",
                            _gain.Left,
                            value);
                    }
                    break;

                case StereoProcessorControlReg.VolumeRight: {
                        var value = data & volumeControlMask;
                        _gain.Right = CalcVolumeGain(value);
                        _loggerService.Debug("ADLIBGOLD: Stereo: Final right volume set to {Right}.2fdB {Value}",
                            _gain.Right,
                            value);
                    }
                    break;

                case StereoProcessorControlReg.Bass: {
                        var value = data & filterControlMask;
                        var gainDb = CalcFilterGainDb(value);
                        SetLowShelfGain(gainDb);

                        _loggerService.Debug("ADLIBGOLD: Stereo: Bass gain set to {GainDb}.2fdB {Value}",
                            gainDb,
                            value);
                    }
                    break;

                case StereoProcessorControlReg.Treble: {
                        var value = data & filterControlMask;
                        // Additional treble boost to make the emulated sound more
                        // closely resemble real hardware recordings.
                        const int extraTreble = 1;
                        var gainDb = CalcFilterGainDb(value + extraTreble);
                        SetHighShelfGain(gainDb);

                        _loggerService.Debug("ADLIBGOLD: Stereo: Treble gain set to {GainDb}.2fdB {Value}",
                            gainDb,
                            value);
                    }
                    break;

                case StereoProcessorControlReg.SwitchFunctions: {
                        var sf = new StereoProcessorSwitchFunctions(data);
                        _sourceSelector = (StereoProcessorSourceSelector)sf.SourceSelector;
                        _stereoMode = (StereoProcessorStereoMode)sf.StereoMode;
                        _loggerService.Debug("ADLIBGOLD: Stereo: Source selector set to {SourceSelector}, stereo mode set to {StereoMode}",
                            (int)(_sourceSelector),
                            (int)(_stereoMode));
                    }
                    break;
            }
        }

        public void SetHighShelfGain(double gainDb) {
            const double cutOffFrequency = 2500.0;
            const double slope = 0.5;
            foreach (HighShelf f in _highShelf) {
                f.Setup(_sampleRate, cutOffFrequency, gainDb, slope);
            }
        }

        public void SetLowShelfGain(double gainDb) {
            const double cutoff_freq = 400.0;
            const double slope = 0.5;
            foreach (LowShelf f in _lowShelf) {
                f.Setup(_sampleRate, cutoff_freq, gainDb, slope);
            }
        }

        public AudioFrame ProcessSourceSelection(AudioFrame frame) {
            return _sourceSelector switch {
                StereoProcessorSourceSelector.SoundA1 or StereoProcessorSourceSelector.SoundA2 => new(frame.Left, frame.Left),
                StereoProcessorSourceSelector.SoundB1 or StereoProcessorSourceSelector.SoundB2 => new(frame.Right, frame.Right),
                _ => frame,// Dune sends an invalid source selector value of 0 during the
                           // intro; we'll just revert to stereo operation
            };
        }

        public AudioFrame ProcessShelvingFilters(AudioFrame frame) {
            AudioFrame out_frame = new();

            for (int i = 0; i < 2; ++i) {
                out_frame[i] = (float)_lowShelf[i].Filter(frame[i]);
                out_frame[i] = (float)_highShelf[i].Filter(out_frame[i]);
            }
            return out_frame;
        }

        public AudioFrame ProcessStereoProcessing(AudioFrame frame) {
            AudioFrame out_frame = new();

            switch (_stereoMode) {
                case StereoProcessorStereoMode.ForcedMono: {
                        float m = frame.Left + frame.Right;
                        out_frame.Left = m;
                        out_frame.Right = m;
                    }
                    break;

                case StereoProcessorStereoMode.PseudoStereo:
                    out_frame.Left = (float)_allPass.Filter(frame.Left);
                    out_frame.Right = frame.Right;
                    break;

                case StereoProcessorStereoMode.SpatialStereo: {
                        const float crosstalk_percentage = 52.0f;
                        const float k = crosstalk_percentage / 100.0f;
                        float l = frame.Left;
                        float r = frame.Right;
                        out_frame.Left = l + (l - r) * k;
                        out_frame.Right = r + (r - l) * k;
                    }
                    break;

                case StereoProcessorStereoMode.LinearStereo:
                default: out_frame = frame; break;
            }
            return out_frame;
        }
    }

    [Union]
    private partial record SurroundControlReg {
        public byte Data { get; set; }
        public byte Din { get; set; }
        public byte Sci { get; set; }
        public byte A0 { get; set; }
    }

    /// <summary>
    /// Yamaha YM7128B Surround Processor emulation
    /// </summary>
    private class SurroundProcessor {
        private ChipIdeal _chip = new();

        private ControlState _ctrlState = new();

        private struct ControlState {
            public byte Sci { get; set; }
            public byte A0 { get; set; }
            public byte Addr { get; set; }
            public byte Data { get; set; }
        }

        public SurroundProcessor(ushort sampleRate) {
            if (sampleRate < 10) {
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            }

            YM7128B.ChipIdealSetup(ref _chip, sampleRate);
            YM7128B.ChipIdealReset(ref _chip);
            YM7128B.ChipIdealStart(ref _chip);
        }

        public AudioFrame Process(ref AudioFrame frame) {
            ChipIdealProcessData data = new();
            data.Inputs[0] = frame.Left + frame.Right;
            YM7128B.ChipIdealProcess(ref _chip, ref data);
            return new(data.Outputs[0], data.Outputs[1]);
        }

        public void ControlWrite(byte val) {
            SurroundControlReg reg = new() {
                Data = val,
                A0 = val,
                Din = val,
                Sci = val
            };

            // Change register data at the falling edge of 'a0' word clock
            if (_ctrlState.A0 == 1 && reg.A0 == 0) {
                //		_logger.Debug("ADLIBGOLD: Surround: Write
                // control register %d, data: %d",
                // control_state.addr, control_state.data);

                YM7128B.ChipIdealWrite(ref _chip, _ctrlState.Addr, _ctrlState.Data);
            } else {
                // Data is sent in serially through 'din' in MSB->LSB order,
                // synchronised by the 'sci' bit clock. Data should be read on
                // the rising edge of 'sci'.
                if (_ctrlState.Sci == 0 && reg.Sci == 1) {
                    // The 'a0' word clock determines the type of the data.
                    if (reg.A0 == 1) {
                        // Data cycle
                        _ctrlState.Data = (byte)((_ctrlState.Data << 1) | reg.Din);
                    } else {
                        // Address cycle
                        _ctrlState.Addr = (byte)((_ctrlState.Addr << 1) | reg.Din);
                    }
                }
            }

            _ctrlState.Sci = reg.Sci;
            _ctrlState.A0 = reg.A0;
        }
    }
}