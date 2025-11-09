namespace Spice86.Libs.Sound.Devices.AdlibGold;

using Serilog;

using Spice86.Libs.Sound.Common;
using Spice86.Libs.Sound.Filters.IirFilters.Filters.RBJ;

/// <summary>
///     Identifies the signal source selected by the stereo processor.
/// </summary>
internal enum StereoProcessorSourceSelector : byte {
    /// <summary>
    ///     Uses input channel A, position 1.
    /// </summary>
    SoundA1 = 2,

    /// <summary>
    ///     Uses input channel A, position 2.
    /// </summary>
    SoundA2 = 3,

    /// <summary>
    ///     Uses input channel B, position 1.
    /// </summary>
    SoundB1 = 4,

    /// <summary>
    ///     Uses input channel B, position 2.
    /// </summary>
    SoundB2 = 5,

    /// <summary>
    ///     Uses the primary stereo input.
    /// </summary>
    Stereo1 = 6,

    /// <summary>
    ///     Uses the secondary stereo input.
    /// </summary>
    Stereo2 = 7
}

/// <summary>
///     Describes the stereo matrix applied to the selected source.
/// </summary>
internal enum StereoProcessorStereoMode : byte {
    /// <summary>
    ///     Mixes both channels together into mono.
    /// </summary>
    ForcedMono = 0,

    /// <summary>
    ///     Passes the stereo channels through unchanged.
    /// </summary>
    LinearStereo = 1,

    /// <summary>
    ///     Applies an all-pass filter to create pseudo-stereo depth.
    /// </summary>
    PseudoStereo = 2,

    /// <summary>
    ///     Introduces crosstalk for the spatial stereo effect.
    /// </summary>
    SpatialStereo = 3
}

/// <summary>
///     Represents the packed switch-function register format.
/// </summary>
internal readonly struct StereoProcessorSwitchFunctions(byte data) {
    /// <summary>
    ///     Gets the encoded source selector value.
    /// </summary>
    internal byte SourceSelector => (byte)(data & 0x07);

    /// <summary>
    ///     Gets the encoded stereo mode value.
    /// </summary>
    internal byte StereoMode => (byte)((data >> 3) & 0x03);

    /// <summary>
    ///     Combines the source selector and stereo mode into a packed byte.
    /// </summary>
    internal static byte Compose(byte sourceSelector, byte stereoMode) {
        return (byte)((sourceSelector & 0x07) | ((stereoMode & 0x03) << 3));
    }
}

/// <summary>
///     Implements the AdLib Gold stereo processor, including tone controls and stereo field shaping.
/// </summary>
internal sealed class StereoProcessor {
    private const int Volume0DbValue = 60;
    private const int ShelfFilter0DbValue = 6;
    private readonly AllPass _allPass = new();
    private readonly HighShelf[] _highShelf = [new(), new()];

    private readonly ILogger _logger;
    private readonly LowShelf[] _lowShelf = [new(), new()];
    private readonly int _sampleRateHz;
    private AudioFrame _gain;
    private StereoProcessorSourceSelector _sourceSelector;
    private StereoProcessorStereoMode _stereoMode;

    /// <summary>
    ///     Initializes a new <see cref="StereoProcessor" /> instance.
    /// </summary>
    /// <param name="sampleRateHz">The host sample rate used to configure the filters.</param>
    /// <param name="logger">Logger used to report configuration changes.</param>
    internal StereoProcessor(int sampleRateHz, ILogger logger) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRateHz);
        _sampleRateHz = sampleRateHz;
        _logger = logger.ForContext<StereoProcessor>();

        const double allpassFreqHz = 400.0;
        const double qFactor = 1.7;
        _allPass.Setup(sampleRateHz, allpassFreqHz, qFactor);

        Reset();
        _logger.Debug("Stereo processor initialized at sample rate {SampleRateHz}", sampleRateHz);
    }

    /// <summary>
    ///     Restores the default register values for the stereo processor.
    /// </summary>
    private void Reset() {
        _logger.Debug("Resetting stereo processor at sample rate {SampleRateHz}", _sampleRateHz);
        ResetFilters();
        ControlWrite(StereoProcessorControlReg.VolumeLeft, Volume0DbValue);
        ControlWrite(StereoProcessorControlReg.VolumeRight, Volume0DbValue);
        ControlWrite(StereoProcessorControlReg.Bass, ShelfFilter0DbValue);
        ControlWrite(StereoProcessorControlReg.Treble, ShelfFilter0DbValue);

        byte data = StereoProcessorSwitchFunctions.Compose(
            (byte)StereoProcessorSourceSelector.Stereo1,
            (byte)StereoProcessorStereoMode.LinearStereo);
        ControlWrite(StereoProcessorControlReg.SwitchFunctions, data);
        ResetFilters();
    }

    /// <summary>
    ///     Handles a write to one of the stereo processor's control registers.
    /// </summary>
    /// <param name="reg">The register that was targeted.</param>
    /// <param name="data">The raw register value.</param>
    internal void ControlWrite(StereoProcessorControlReg reg, byte data) {
        const int volumeControlWidth = 6;
        const int volumeControlMask = (1 << volumeControlWidth) - 1;
        const int filterControlWidth = 4;
        const int filterControlMask = (1 << filterControlWidth) - 1;

        switch (reg) {
            case StereoProcessorControlReg.VolumeLeft: {
                int value = data & volumeControlMask;
                _gain.Left = CalcVolumeGain(value);
                LogRegisterWrite(reg, data);
                break;
            }
            case StereoProcessorControlReg.VolumeRight: {
                int value = data & volumeControlMask;
                _gain.Right = CalcVolumeGain(value);
                LogRegisterWrite(reg, data);
                break;
            }
            case StereoProcessorControlReg.Bass: {
                int value = data & filterControlMask;
                double gainDb = CalcFilterGainDb(value);
                SetLowShelfGain(gainDb);
                LogRegisterWrite(reg, data);
                break;
            }
            case StereoProcessorControlReg.Treble: {
                int value = data & filterControlMask;
                const int extraTreble = 1;
                double gainDb = CalcFilterGainDb(value + extraTreble);
                SetHighShelfGain(gainDb);
                LogRegisterWrite(reg, data);
                break;
            }
            case StereoProcessorControlReg.SwitchFunctions: {
                var sf = new StereoProcessorSwitchFunctions(data);
                _sourceSelector = (StereoProcessorSourceSelector)sf.SourceSelector;
                _stereoMode = (StereoProcessorStereoMode)sf.StereoMode;
                LogRegisterWrite(reg, data);
                break;
            }

            default:
                _logger.Warning("Unsupported stereo processor register {Register} written with value {Value:X2}", reg,
                    data);
                break;
        }
    }

    /// <summary>
    ///     Emits a verbose log entry describing a handled register write.
    /// </summary>
    private void LogRegisterWrite(StereoProcessorControlReg reg, byte data) {
        _logger.Verbose("Stereo register {Register} updated with value {Value:X2}", reg, data);
    }

    /// <summary>
    ///     Converts a filter register value into a gain adjustment in decibels.
    /// </summary>
    private static double CalcFilterGainDb(int value) {
        const double minGainDb = -12.0;
        const double maxGainDb = 15.0;
        const double stepDb = 3.0;

        int val = value - ShelfFilter0DbValue;
        return Math.Clamp(val * stepDb, minGainDb, maxGainDb);
    }

    /// <summary>
    ///     Converts a volume register value into a linear gain multiplier.
    /// </summary>
    private static float CalcVolumeGain(int value) {
        const float minGainDb = -128.0f;
        const float maxGainDb = 6.0f;
        const float stepDb = 2.0f;

        int val = value - Volume0DbValue;
        float gainDb = Math.Clamp(val * stepDb, minGainDb, maxGainDb);
        return MathEx.DecibelToGain(gainDb);
    }

    /// <summary>
    ///     Configures the low-shelf filters using the specified gain.
    /// </summary>
    /// <param name="gainDb">The gain, in decibels, applied to low frequencies.</param>
    private void SetLowShelfGain(double gainDb) {
        const double cutoffHz = 400.0;
        const double slope = 0.5;
        for (int i = 0; i < _lowShelf.Length; i++) {
            _lowShelf[i].Setup(_sampleRateHz, cutoffHz, gainDb, slope);
        }
    }

    /// <summary>
    ///     Configures the high-shelf filters using the specified gain.
    /// </summary>
    /// <param name="gainDb">The gain, in decibels, applied to high frequencies.</param>
    private void SetHighShelfGain(double gainDb) {
        const double cutoffHz = 2500.0;
        const double slope = 0.5;
        for (int i = 0; i < _highShelf.Length; i++) {
            _highShelf[i].Setup(_sampleRateHz, cutoffHz, gainDb, slope);
        }
    }

    /// <summary>
    ///     Resets all shelving filters and the all-pass filter to their initial state.
    /// </summary>
    private void ResetFilters() {
        for (int i = 0; i < _lowShelf.Length; i++) {
            _lowShelf[i].Reset();
        }

        for (int i = 0; i < _highShelf.Length; i++) {
            _highShelf[i].Reset();
        }

        _allPass.Reset();
    }

    /// <summary>
    ///     Applies the current source-selection logic to the frame.
    /// </summary>
    /// <param name="frame">The input frame.</param>
    /// <returns>The re-routed frame.</returns>
    private void ProcessSourceSelection(ref AudioFrame frame) {
        switch (_sourceSelector) {
            case StereoProcessorSourceSelector.SoundA1:
            case StereoProcessorSourceSelector.SoundA2: {
                float left = frame.Left;
                frame.Left = left;
                frame.Right = left;
                break;
            }
            case StereoProcessorSourceSelector.SoundB1:
            case StereoProcessorSourceSelector.SoundB2: {
                float right = frame.Right;
                frame.Left = right;
                frame.Right = right;
                break;
            }
        }
    }

    /// <summary>
    ///     Passes the frame through the low and high shelf filters for each channel.
    /// </summary>
    /// <param name="frame">The input frame.</param>
    /// <returns>The filtered frame.</returns>
    private void ProcessShelvingFilters(ref AudioFrame frame) {
        float left = frame.Left;
        left = _lowShelf[0].Filter(left);
        left = _highShelf[0].Filter(left);
        frame.Left = left;

        float right = frame.Right;
        right = _lowShelf[1].Filter(right);
        right = _highShelf[1].Filter(right);
        frame.Right = right;
    }

    /// <summary>
    ///     Applies the selected stereo processing mode.
    /// </summary>
    /// <param name="frame">The frame to process.</param>
    /// <returns>The processed frame.</returns>
    private void ProcessStereoProcessing(ref AudioFrame frame) {
        switch (_stereoMode) {
            case StereoProcessorStereoMode.ForcedMono: {
                float mono = frame.Left + frame.Right;
                frame.Left = mono;
                frame.Right = mono;
                break;
            }
            case StereoProcessorStereoMode.PseudoStereo: {
                frame.Left = _allPass.Filter(frame.Left);
                break;
            }
            case StereoProcessorStereoMode.SpatialStereo: {
                const float crosstalkPercentage = 52.0f;
                const float k = crosstalkPercentage / 100.0f;
                float l = frame.Left;
                float r = frame.Right;
                frame.Left = l + ((l - r) * k);
                frame.Right = r + ((r - l) * k);
                break;
            }
            case StereoProcessorStereoMode.LinearStereo:
                break;
            default:
                _logger.Warning("Unsupported stereo mode {StereoMode} encountered. Leaving frame unchanged.",
                    _stereoMode);
                break;
        }
    }

    /// <summary>
    ///     Runs a frame through the stereo processor signal chain.
    /// </summary>
    /// <param name="frame">The frame to process.</param>
    /// <returns>The processed frame.</returns>
    internal void Process(ref AudioFrame frame) {
        ProcessSourceSelection(ref frame);
        ProcessShelvingFilters(ref frame);
        ProcessStereoProcessing(ref frame);
        frame.Left *= _gain.Left;
        frame.Right *= _gain.Right;
    }
}
