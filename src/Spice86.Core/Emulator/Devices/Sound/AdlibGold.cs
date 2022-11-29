namespace Spice86.Core.Emulator.Devices.Sound;

using Dunet;

using Serilog;

using Spice86.Core.Backend.Audio;
using Spice86.Core.Backend.Audio.Iir;
using Spice86.Core.CLI;
using Spice86.Core.Emulator.Devices.Sound.Ym7128b;
using Spice86.Core.Emulator.Devices.Sound.Ymf262Emu;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Sound;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// Adlib Gold implementation, translated from DOSBox Staging code
/// </summary>
public class AdlibGold : OPL3FM {
    public AdlibGold(Machine machine, Configuration configuration) : base(machine, configuration) {
    }

    private enum StereoProcessorControlReg {
        VolumeLeft,
        VolumeRight,
        Bass,
        Treble,
        SwitchFunctions,
    }

    [Union]
    private partial record StereoProcessorSwitchFunctions {
        public StereoProcessorSwitchFunctions(byte value) {
            Data = value;
            Source_Selector = value;
            Stereo_Mode = value;
        }

        public StereoProcessorSwitchFunctions() { }

        public byte Data { get; set; }
        public byte Source_Selector { get; set; }
        public byte Stereo_Mode { get; set; }
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
        public AudioFrame(double left, double right) {
            Left = left;
            Right = right;
        }

        public double Left { get; set; }

        public double Right { get; set; }

        public double this[int i] {
            get { return int.IsEvenInteger(i) ? Left : Right; }
            set { if (int.IsEvenInteger(i)) { Left = value; } else { Right = value; } }
        }
    }

    /// <summary>
    /// Philips Semiconductors TDA8425 hi-fi stereo audio processor emulation
    /// </summary>
    private class StereoProcessor {
        private readonly ushort sample_rate = 0;
        private AudioFrame gain = new();
        private StereoProcessorSourceSelector source_selector = new();
        private StereoProcessorStereoMode stereo_mode = new();

        // Stero low and high-shelf filters
        private readonly LowShelf[] lowshelf = new LowShelf[2];
        private readonly HighShelf[] highshelf = new HighShelf[2];
        readonly AllPass allpass = new();

        private const int volume_0db_value = 60;

        private const int shelf_filter_0db_value = 6;

        private ILoggerService _loggerService;

        public StereoProcessor(ushort _sample_rate, ILoggerService loggerService) {
            _loggerService = loggerService;
            sample_rate = _sample_rate;
            if (sample_rate <= 0) {
                throw new IndexOutOfRangeException(nameof(_sample_rate));
            }

            const double allpass_freq = 400.0;
            const double q_factor = 1.7;
            allpass.setup(sample_rate, allpass_freq, q_factor);
            Reset();
        }

        public void Reset() {
            ControlWrite(StereoProcessorControlReg.VolumeLeft, volume_0db_value);
            ControlWrite(StereoProcessorControlReg.VolumeRight, volume_0db_value);
            ControlWrite(StereoProcessorControlReg.Bass, shelf_filter_0db_value);
            ControlWrite(StereoProcessorControlReg.Treble, shelf_filter_0db_value);
            StereoProcessorSwitchFunctions sf = new() {
                Source_Selector = (byte)StereoProcessorSourceSelector.Stereo1,
                Stereo_Mode = (byte)StereoProcessorStereoMode.LinearStereo
            };
            ControlWrite(StereoProcessorControlReg.SwitchFunctions, sf.Data);
        }

        public void ControlWrite(
            StereoProcessorControlReg reg,
            byte data) {
            double calc_volume_gain(int value) {
                const float min_gain_db = -128.0f;
                const float max_gain_db = 6.0f;
                const float step_db = 2.0f;

                var val = (float)(value - volume_0db_value);
                var gain_db = Math.Clamp(val * step_db, min_gain_db, max_gain_db);
                return MathUtils.decibel_to_gain(gain_db);
            }

            double calc_filter_gain_db(int value) {
                const double min_gain_db = -12.0;
                const double max_gain_db = 15.0;
                const double step_db = 3.0;

                var val = value - shelf_filter_0db_value;
                return Math.Clamp(val * step_db, min_gain_db, max_gain_db);
            }

            const int volume_control_width = 6;
            const int volume_control_mask = (1 << volume_control_width) - 1;

            const int filter_control_width = 4;
            const int filter_control_mask = (1 << filter_control_width) - 1;

            switch (reg) {
                case StereoProcessorControlReg.VolumeLeft: {
                        var value = data & volume_control_mask;
                        gain.Left = calc_volume_gain(value);
                        _loggerService.Debug("ADLIBGOLD: Stereo: Final left volume set to {Left}.2fdB {Value}",
                            gain.Left,
                            value);
                    }
                    break;

                case StereoProcessorControlReg.VolumeRight: {
                        var value = data & volume_control_mask;
                        gain.Right = calc_volume_gain(value);
                        _loggerService.Debug("ADLIBGOLD: Stereo: Final right volume set to {Right}.2fdB {Value}",
                            gain.Right,
                            value);
                    }
                    break;

                case StereoProcessorControlReg.Bass: {
                        var value = data & filter_control_mask;
                        var gain_db = calc_filter_gain_db(value);
                        SetLowShelfGain(gain_db);

                        _loggerService.Debug("ADLIBGOLD: Stereo: Bass gain set to {GainDb}.2fdB {Value}",
                            gain_db,
                            value);
                    }
                    break;

                case StereoProcessorControlReg.Treble: {
                        var value = data & filter_control_mask;
                        // Additional treble boost to make the emulated sound more
                        // closely resemble real hardware recordings.
                        const int extra_treble = 1;
                        var gain_db = calc_filter_gain_db(value + extra_treble);
                        SetHighShelfGain(gain_db);

                        _loggerService.Debug("ADLIBGOLD: Stereo: Treble gain set to {GainDb}.2fdB {Value}",
                            gain_db,
                            value);
                    }
                    break;

                case StereoProcessorControlReg.SwitchFunctions: {
                        var sf = new StereoProcessorSwitchFunctions(data);
                        source_selector =  (StereoProcessorSourceSelector)sf.Source_Selector;
                        stereo_mode = (StereoProcessorStereoMode)sf.Stereo_Mode;
                        _loggerService.Debug("ADLIBGOLD: Stereo: Source selector set to {SourceSelector}, stereo mode set to {StereoMode}",
                            (int)(source_selector),
                            (int)(stereo_mode));
                    }
                    break;
            }
        }

        public void SetHighShelfGain(double gain_db) {
            const double cutoff_freq = 2500.0;
            const double slope = 0.5;
            foreach (HighShelf f in highshelf) {
                f.setup(sample_rate, cutoff_freq, gain_db, slope);
            }
        }

        public void SetLowShelfGain(double gain_db) {
            const double cutoff_freq = 400.0;
            const double slope = 0.5;
            foreach(LowShelf f in lowshelf) {
                f.setup(sample_rate, cutoff_freq, gain_db, slope);
            }
        }

        public AudioFrame ProcessSourceSelection(AudioFrame frame) {
            return source_selector switch {
                StereoProcessorSourceSelector.SoundA1 or StereoProcessorSourceSelector.SoundA2 => new(frame.Left, frame.Left),
                StereoProcessorSourceSelector.SoundB1 or StereoProcessorSourceSelector.SoundB2 => new(frame.Right, frame.Right),
                _ => frame,// Dune sends an invalid source selector value of 0 during the
                           // intro; we'll just revert to stereo operation
            };
        }

        public AudioFrame ProcessShelvingFilters(AudioFrame frame) {
            AudioFrame out_frame = new();

            for (int i = 0; i < 2; ++i) {
                out_frame[i] = lowshelf[i].filter(frame[i]);
                out_frame[i] = highshelf[i].filter(out_frame[i]);
            }
            return out_frame;
        }

        public AudioFrame ProcessStereoProcessing(AudioFrame frame) {
            AudioFrame out_frame = new();

            switch (stereo_mode) {
                case StereoProcessorStereoMode.ForcedMono: {
                        double m = frame.Left + frame.Right;
                        out_frame.Left = m;
                        out_frame.Right = m;
                    }
                    break;

                case StereoProcessorStereoMode.PseudoStereo:
                    out_frame.Left = allpass.filter(frame.Left);
                    out_frame.Right = frame.Right;
                    break;

                case StereoProcessorStereoMode.SpatialStereo: {
                        const float crosstalk_percentage = 52.0f;
                        const float k = crosstalk_percentage / 100.0f;
                        double l = frame.Left;
                        double r = frame.Right;
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
        public byte data { get; set; }
        public byte din { get; set; }
        public byte sci { get; set; }
        public byte a0 { get; set; }
    }

    /// <summary>
    /// Yamaha YM7128B Surround Processor emulation
    /// </summary>
    private class SurroundProcessor {
        private YM7128B_ChipIdeal chip = new();

        private ControlState control_state = new();

        private struct ControlState {
            public byte sci { get; set; }
            public byte a0 { get; set; }
            public byte addr { get; set; }
            public byte data { get; set; }
        }

        public SurroundProcessor(ushort sample_rate) {
            if (sample_rate < 10) {
                throw new ArgumentOutOfRangeException(nameof(sample_rate));
            }

            YM7128B.YM7128B_ChipIdeal_Setup(ref chip, sample_rate);
            YM7128B.YM7128B_ChipIdeal_Reset(ref chip);
            YM7128B.YM7128B_ChipIdeal_Start(ref chip);
        }

        public void ControlWrite(byte val) {
            SurroundControlReg reg = new() {
                data = val,
                a0 = val,
                din = val,
                sci = val
            };

            // Change register data at the falling edge of 'a0' word clock
            if (control_state.a0 == 1 && reg.a0 == 0) {
                //		_logger.Debug("ADLIBGOLD: Surround: Write
                // control register %d, data: %d",
                // control_state.addr, control_state.data);

                YM7128B.YM7128B_ChipIdeal_Write(ref chip, control_state.addr, control_state.data);
            } else {
                // Data is sent in serially through 'din' in MSB->LSB order,
                // synchronised by the 'sci' bit clock. Data should be read on
                // the rising edge of 'sci'.
                if (control_state.sci == 0 && reg.sci == 1) {
                    // The 'a0' word clock determines the type of the data.
                    if (reg.a0 == 1) {
                        // Data cycle
                        control_state.data = (byte)((control_state.data << 1) | reg.din);
                    } else {
                        // Address cycle
                        control_state.addr = (byte)((control_state.addr << 1) | reg.din);
                    }
                }
            }

            control_state.sci = reg.sci;
            control_state.a0 = reg.a0;
        }
    }
}