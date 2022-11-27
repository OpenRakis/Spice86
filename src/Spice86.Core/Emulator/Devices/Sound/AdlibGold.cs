namespace Spice86.Core.Emulator.Devices.Sound;

using Dunet;

using Spice86.Core.Backend.Audio.Iir;
using Spice86.Core.Emulator.Devices.Sound.Ym7128b;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// Adlib Gold implementation
/// </summary>
public class AdlibGold : DefaultIOPortHandler {
    private YM7128B_ChipIdeal chip = new();

    private struct ControlState {
        public byte Sci { get; set; }
        public byte A0 { get; set; }
        public byte Addr { get; set; }
        public byte Data { get; set; }
    }

    private ControlState control_state = new();

    private enum StereoProcessorControlReg {
        VolumeLeft,
        VolumeRight,
        Bass,
        Treble,
        SwitchFunctions,
    }

    [Union]
    private partial record StereoProcessorSwitchFunctions {
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
        public AudioFrame(double left, double right) : this() {
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

    private class StereoProcessor {
        private byte sample_rate = 0;
        private AudioFrame gain = new();
        private StereoProcessorSourceSelector source_selector = new();
        private StereoProcessorStereoMode stereo_mode = new();

        // Stero low and high-shelf filters
        private readonly LowShelf[] lowshelf = new LowShelf[2];
        private readonly HighShelf[] highshelf = new HighShelf[2];
        AllPass allpass = new();

        public AudioFrame ProcessSourceSelection(AudioFrame frame) {
            switch (source_selector) {
                case StereoProcessorSourceSelector.SoundA1:
                case StereoProcessorSourceSelector.SoundA2:
                    return new(frame.Left, frame.Left);

                case StereoProcessorSourceSelector.SoundB1:
                case StereoProcessorSourceSelector.SoundB2:
                    return new(frame.Right, frame.Right);

                case StereoProcessorSourceSelector.Stereo1:
                case StereoProcessorSourceSelector.Stereo2:
                default:
                    // Dune sends an invalid source selector value of 0 during the
                    // intro; we'll just revert to stereo operation
                    return frame;
            }
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

    public AdlibGold(Machine machine, Configuration configuration, ILoggerService loggerService) : base(machine, configuration, loggerService) {
    }

    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(OPLConsts.FM_MUSIC_DATA_PORT_NUMBER_2, this);
        ioPortDispatcher.AddIOPortHandler(OPLConsts.FM_MUSIC_STATUS_PORT_NUMBER_2, this);
    }

    public override byte ReadByte(int port) {
        return base.ReadByte(port);
    }

    public override void WriteByte(int port, byte value) {
        base.WriteByte(port, value);
    }
}