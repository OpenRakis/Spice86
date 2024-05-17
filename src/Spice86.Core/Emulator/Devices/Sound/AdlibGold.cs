namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Backend.Audio.Iir;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound.Ym7128b;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Interfaces;

using System.Runtime.InteropServices;

/// <summary>
/// Adlib Gold implementation, translated from DOSBox Staging code
/// </summary>
public sealed class AdlibGold  {
    private readonly StereoProcessor _stereoProcessor;
    private readonly SurroundProcessor _surroundProcessor;
    private readonly ushort _sampleRate;

    public AdlibGold(ILoggerService loggerService) {
        _sampleRate = 48000;
        _stereoProcessor = new(_sampleRate, loggerService);
        _surroundProcessor = new(_sampleRate);
    }

    public void StereoControlWrite(byte reg, byte data) => _stereoProcessor.ControlWrite((StereoProcessorControlReg)reg, data);

    public void SurroundControlWrite(byte data) => _surroundProcessor.ControlWrite(data);

    private void Process(short[] input, uint framesRemaining, AudioFrame output) {
        for (var index = 0; framesRemaining-- > 0; index++) {
            AudioFrame frame = new(output.AsSpan());
            AudioFrame wet = _surroundProcessor.Process(frame);

            // Additional wet signal level boost to make the emulated
            // sound more closely resemble real hardware recordings.
            const float wetBoost = 1.8f;
            frame.Left = wet.Left * wetBoost;
            frame.Right = wet.Right * wetBoost;
            frame = _surroundProcessor.Process(frame);

            output[index] = frame.Left;
            output[index + 1] = frame.Right;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct StereoProcessorSwitchFunctions {
        public StereoProcessorSwitchFunctions(byte value) {
            data = value;
            sourceSelector = value;
            stereoMode = value;
        }

        public StereoProcessorSwitchFunctions() { }

        [FieldOffset(0)]
        public byte data;
        [FieldOffset(0)]
        public byte sourceSelector;
        [FieldOffset(0)]
        public byte stereoMode;
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

    /// <summary>
    /// Philips Semiconductors TDA8425 hi-fi stereo audio processor emulation
    /// </summary>
    private class StereoProcessor {
        private readonly ushort _sampleRate = 0;
        //Left and Right channel gain values.
        private float[] _gain = new float[2];
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

            const double allPassFrequency = 400.0;
            const double qFactor = 1.7;
            _allPass.Setup(_sampleRate, allPassFrequency, qFactor);
            Reset();
        }

        public void Reset() {
            ControlWrite(StereoProcessorControlReg.VolumeLeft, Volume0DbValue);
            ControlWrite(StereoProcessorControlReg.VolumeRight, Volume0DbValue);
            ControlWrite(StereoProcessorControlReg.Bass, ShelfFilter0DbValue);
            ControlWrite(StereoProcessorControlReg.Treble, ShelfFilter0DbValue);
            StereoProcessorSwitchFunctions sf = new() {
                sourceSelector = (byte)StereoProcessorSourceSelector.Stereo1,
                stereoMode = (byte)StereoProcessorStereoMode.LinearStereo
            };
            ControlWrite(StereoProcessorControlReg.SwitchFunctions, sf.data);
        }

        public void ControlWrite(
            StereoProcessorControlReg reg,
            byte data) {
            float CalcVolumeGain(int value) {
                const float minGainDb = -128.0f;
                const float maxGainDb = 6.0f;
                const float stepDb = 2.0f;

                float val = value - Volume0DbValue;
                float gainDb = Math.Clamp(val * stepDb, minGainDb, maxGainDb);
                return MathUtils.DecibelToGain(gainDb);
            }

            float CalcFilterGainDb(int value) {
                const double mainGainDb = -12.0;
                const double maxGainDb = 15.0;
                const double stepDb = 3.0;

                int val = value - ShelfFilter0DbValue;
                return (float)Math.Clamp(val * stepDb, mainGainDb, maxGainDb);
            }

            const int volumeControlWidth = 6;
            const int volumeControlMask = (1 << volumeControlWidth) - 1;

            const int filterControlWidth = 4;
            const int filterControlMask = (1 << filterControlWidth) - 1;

            switch (reg) {
                case StereoProcessorControlReg.VolumeLeft: {
                        var value = data & volumeControlMask;
                        _gain[0] = CalcVolumeGain(value);
                        _loggerService.Debug("ADLIBGOLD: Stereo: Final left volume set to {Left}.2fdB {Value}",
                            _gain[0],
                            value);
                    }
                    break;

                case StereoProcessorControlReg.VolumeRight: {
                        var value = data & volumeControlMask;
                        _gain[1] = CalcVolumeGain(value);
                        _loggerService.Debug("ADLIBGOLD: Stereo: Final right volume set to {Right}.2fdB {Value}",
                            _gain[1],
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
                        _sourceSelector = (StereoProcessorSourceSelector)sf.sourceSelector;
                        _stereoMode = (StereoProcessorStereoMode)sf.stereoMode;
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
            const double cutoffFreq = 400.0;
            const double slope = 0.5;
            foreach (LowShelf f in _lowShelf) {
                f.Setup(_sampleRate, cutoffFreq, gainDb, slope);
            }
        }

        public AudioFrame ProcessSourceSelection(AudioFrame frame) {
            return _sourceSelector switch {
                StereoProcessorSourceSelector.SoundA1 or StereoProcessorSourceSelector.SoundA2 => new(frame.AsSpan()[0..0]),
                StereoProcessorSourceSelector.SoundB1 or StereoProcessorSourceSelector.SoundB2 => new(frame.AsSpan()[1..1]),
                _ => frame,// Dune sends an invalid source selector value of 0 during the
                           // intro; we'll just revert to stereo operation
            };
        }

        public AudioFrame ProcessShelvingFilters(AudioFrame frame) {
            AudioFrame outFrame = new();

            for (int i = 0; i < 2; ++i) {
                outFrame[i] = (float)_lowShelf[i].Filter(frame[i]);
                outFrame[i] = (float)_highShelf[i].Filter(outFrame[i]);
            }
            return outFrame;
        }

        public AudioFrame ProcessStereoProcessing(AudioFrame frame) {
            AudioFrame outFrame = new();

            switch (_stereoMode) {
                case StereoProcessorStereoMode.ForcedMono: {
                        float m = frame.Left + frame.Right;
                        outFrame.Left = m;
                        outFrame.Right = m;
                    }
                    break;

                case StereoProcessorStereoMode.PseudoStereo:
                    outFrame.Left = (float)_allPass.Filter(frame.Left);
                    outFrame.Right = frame.Right;
                    break;

                case StereoProcessorStereoMode.SpatialStereo: {
                        const float crosstalkPercentage = 52.0f;
                        const float k = crosstalkPercentage / 100.0f;
                        float l = frame.Left;
                        float r = frame.Right;
                        outFrame.Left = l + (l - r) * k;
                        outFrame.Right = r + (r - l) * k;
                    }
                    break;

                case StereoProcessorStereoMode.LinearStereo:
                default: outFrame = frame; break;
            }
            return outFrame;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct SurroundControlReg {
        [FieldOffset(0)]
        public byte data;
        [FieldOffset(0)]
        public byte din;
        [FieldOffset(0)]
        public byte sci;
        [FieldOffset(0)]
        public byte a0;
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

            Ym7128B.ChipIdealSetup(ref _chip, sampleRate);
            Ym7128B.ChipIdealReset(ref _chip);
            Ym7128B.ChipIdealStart(ref _chip);
        }

        public AudioFrame Process(AudioFrame frame) {
            ChipIdealProcessData data = new();
            data.Inputs[0] = frame.Left + frame.Right;
            Ym7128B.ChipIdealProcess(ref _chip, ref data);
            return new(data.Outputs);
        }

        public void ControlWrite(byte val) {
            SurroundControlReg reg = new() {
                data = val,
                a0 = val,
                din = val,
                sci = val
            };

            // Change register data at the falling edge of 'a0' word clock
            if (_ctrlState.A0 == 1 && reg.a0 == 0) {
                //		_logger.Debug("ADLIBGOLD: Surround: Write
                // control register %d, data: %d",

                Ym7128B.ChipIdealWrite(ref _chip, _ctrlState.Addr, _ctrlState.Data);
                Ym7128B.ChipIdealWrite(ref _chip, _ctrlState.Addr, _ctrlState.Data);
            } else {
                // Data is sent in serially through 'din' in MSB->LSB order,
                // synchronised by the 'sci' bit clock. Data should be read on
                // the rising edge of 'sci'.
                if (_ctrlState.Sci == 0 && reg.sci == 1) {
                    // The 'a0' word clock determines the type of the data.
                    if (reg.a0 == 1) {
                        // Data cycle
                        _ctrlState.Data = (byte)((_ctrlState.Data << 1) | reg.din);
                    } else {
                        // Address cycle
                        _ctrlState.Addr = (byte)((_ctrlState.Addr << 1) | reg.din);
                    }
                }
            }

            _ctrlState.Sci = reg.sci;
            _ctrlState.A0 = reg.a0;
        }
    }
}