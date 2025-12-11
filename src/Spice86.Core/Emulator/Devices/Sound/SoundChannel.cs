namespace Spice86.Core.Emulator.Devices.Sound;

using Serilog.Events;

using Spice86.Shared.Interfaces;

/// <summary>
///     Represents a sound channel and coordinates rendering with the shared software mixer.
/// </summary>
public class SoundChannel {
    private readonly ILoggerService _logger;
    private readonly SoftwareMixer _mixer;
    private bool _isMuted;
    private float _stereoSeparation = 50;
    private int _volume = 100;
    private Action? _renderCallback;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SoundChannel" /> class.
    /// </summary>
    /// <param name="mixer">The software mixer that will render the channel.</param>
    /// <param name="logger">The logger used for diagnostics.</param>
    /// <param name="name">The name of the sound channel.</param>
    /// <param name="sampleRate">The channel sample rate, in Hz.</param>
    public SoundChannel(SoftwareMixer mixer, ILoggerService logger, string name, int sampleRate = 48000) {
        _mixer = mixer;
        _logger = logger;
        Name = name;
        SampleRate = sampleRate;
        _mixer.Register(this, SampleRate);

        _logger.Debug(
            "SOUND CHANNEL {ChannelName}: Initialized with sample rate {SampleRate}Hz and stereo separation {StereoSeparation}%.",
            Name, SampleRate, _stereoSeparation);
    }

    /// <summary>
    ///     Gets or sets the stereo separation percentage. Values outside the range 0-100 are clamped and logged.
    /// </summary>
    public float StereoSeparation {
        get => _stereoSeparation;
        set => _stereoSeparation = Math.Clamp(value, 0, 100);
    }

    /// <summary>
    ///     Gets the name of the sound channel.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Gets the sample rate used by this sound channel, in Hz.
    /// </summary>
    public int SampleRate { get; }

    /// <summary>
    ///     Gets or sets a value indicating whether the sound channel is muted.
    /// </summary>
    public bool IsMuted {
        get => _isMuted;
        set {
            if (_isMuted == value) {
                return;
            }

            _isMuted = value;
            _logger.Information(
                "SOUND CHANNEL {ChannelName}: Muted state changed to {Muted}.",
                Name, _isMuted);
        }
    }

    /// <summary>
    ///     Gets or sets the volume, as a percentage. Values outside the range 0-100 are clamped and logged.
    /// </summary>
    public int Volume {
        get => _volume;
        set {
            int clamped = Math.Clamp(value, 0, 100);
            if (clamped != value) {
                _logger.Warning(
                    "SOUND CHANNEL {ChannelName}: Volume request {RequestedVolume}% out of range; clamped to {ClampedVolume}%.",
                    Name, value, clamped);
            }

            if (_volume == clamped) {
                return;
            }

            _volume = clamped;

            if (_logger.IsEnabled(LogEventLevel.Debug)) {
                _logger.Debug(
                    "SOUND CHANNEL {ChannelName}: Volume set to {Volume}%.",
                    Name, _volume);
            }
        }
    }

    /// <summary>
    ///     Renders a 32-bit floating-point audio frame through the underlying mixer.
    /// </summary>
    /// <param name="data">The audio frame to mix and render.</param>
    public void Render(Span<float> data) {
        _mixer.Render(data, this);
    }

    /// <summary>
    ///     Renders a 16-bit signed audio frame through the underlying mixer.
    /// </summary>
    /// <param name="data">The audio frame to mix and render.</param>
    public void Render(Span<short> data) {
        _mixer.Render(data, this);
    }

    /// <summary>
    ///     Renders an 8-bit unsigned audio frame through the underlying mixer.
    /// </summary>
    /// <param name="data">The audio frame to mix and render.</param>
    public void Render(Span<byte> data) {
        _mixer.Render(data, this);
    }

    /// <summary>
    ///     Sets the callback that the mixer will invoke to generate audio data.
    /// </summary>
    /// <param name="callback">The callback action to invoke when the mixer needs audio data.</param>
    public void SetRenderCallback(Action? callback) {
        _renderCallback = callback;
    }

    /// <summary>
    ///     Invokes the render callback if one is set.
    /// </summary>
    internal void InvokeRenderCallback() {
        _renderCallback?.Invoke();
    }
}