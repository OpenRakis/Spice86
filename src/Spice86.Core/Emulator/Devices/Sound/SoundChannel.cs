namespace Spice86.Core.Emulator.Devices.Sound;

using Serilog.Events;

using Spice86.Shared.Interfaces;

/// <summary>
///     Represents a sound channel and coordinates rendering with the shared software mixer.
///     Each channel has its own render thread to prevent packet drops in PortAudio.
///     Implements DOSBox Staging's RenderUpToNow pattern: time-based sample generation.
/// </summary>
public class SoundChannel {
    private readonly ILoggerService _logger;
    private readonly SoftwareMixer _mixer;
    private bool _isMuted;
    private float _stereoSeparation = 50;
    private int _volume = 100;
    private Action? _renderCallback;
    private Thread? _renderThread;
    private readonly ManualResetEventSlim _stopEvent = new(false);
    private readonly System.Diagnostics.Stopwatch _renderTimer = new();
    private double _lastRenderTimeSeconds;
    private double _fractionalSamples;

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

    /// <summary>
    ///     Starts the render thread for this channel.
    /// </summary>
    internal void StartRenderThread() {
        if (_renderThread is not null) {
            return;
        }

        _stopEvent.Reset();
        _renderThread = new Thread(RenderThreadLoop) {
            Name = $"SoundChannel-{Name}",
            IsBackground = true
        };
        _renderThread.Start();
        
        _logger.Debug("SOUND CHANNEL {ChannelName}: Render thread started.", Name);
    }

    /// <summary>
    ///     Stops the render thread for this channel.
    /// </summary>
    internal void StopRenderThread() {
        if (_renderThread is null) {
            return;
        }

        _stopEvent.Set();
        if (_renderThread.IsAlive) {
            _renderThread.Join(TimeSpan.FromSeconds(2));
        }
        _renderThread = null;
        
        _logger.Debug("SOUND CHANNEL {ChannelName}: Render thread stopped.", Name);
    }

    /// <summary>
    ///     The render thread loop implementing DOSBox Staging's RenderUpToNow pattern.
    ///     Calculates required samples based on elapsed real time, not fixed intervals or tight loops.
    ///     No sleep - checks continuously but only renders when enough time elapsed.
    ///     No try/catch - errors propagate for proper handling.
    /// </summary>
    private void RenderThreadLoop() {
        _renderTimer.Start();
        _lastRenderTimeSeconds = 0;
        _fractionalSamples = 0;
        
        // RenderUpToNow pattern from DOSBox Staging:
        // Calculate elapsed time and only render when enough samples are needed
        const int typicalBufferSize = 2048;
        double samplesPerCallback = typicalBufferSize;
        
        while (!_stopEvent.IsSet) {
            // 1. Calculate elapsed time since last render
            double currentTimeSeconds = _renderTimer.Elapsed.TotalSeconds;
            double elapsedSeconds = currentTimeSeconds - _lastRenderTimeSeconds;
            
            // 2. Calculate how many samples should have been generated in this time
            //    samples = sampleRate × elapsedTime
            double samplesNeeded = (SampleRate * elapsedSeconds) + _fractionalSamples;
            
            // 3. Only render if we need at least one full callback's worth of samples
            //    Devices generate fixed-size buffers, so we call callback when enough time has passed
            //    At 48kHz with 2048 sample buffer: 2048/48000 = 42.67ms per callback
            if (samplesNeeded >= samplesPerCallback) {
                // Invoke callback to generate audio
                _renderCallback?.Invoke();
                
                // Update timing - consumed one callback's worth of samples
                _fractionalSamples = samplesNeeded - samplesPerCallback;
                _lastRenderTimeSeconds = currentTimeSeconds;
            }
            // Note: No sleep - continuously check, but only render when time elapsed
            // The callback itself (PortAudio WriteData) will block when buffer is full
        }
    }
}