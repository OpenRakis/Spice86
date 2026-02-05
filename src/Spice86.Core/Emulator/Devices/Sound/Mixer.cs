namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Backend.Audio;
using Spice86.Shared.Interfaces;

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

using AudioFrame = Spice86.Libs.Sound.Common.AudioFrame;
using HighPassFilter = Spice86.Libs.Sound.Filters.IirFilters.Filters.Butterworth.HighPass;

/// <summary>
/// Central audio mixer that runs in its own thread and produces final mixed output.
/// </summary>
public sealed class Mixer : IDisposable {
    private const int DefaultSampleRateHz = 48000;
    private const int DefaultBlocksize = 1024;
    private const int MaxPrebufferMs = 100;

    // This shows up nicely as 50% and -6.00 dB in the MIXER command's output
    private const float Minus6db = 0.501f;

    private readonly ILoggerService _loggerService;
    private readonly AudioPlayerFactory _audioPlayerFactory;
    private readonly AudioPlayer _audioPlayer;

    // Channels registry - matches DOSBox mixer.channels
    private readonly ConcurrentDictionary<string, MixerChannel> _channels = new();

    // Mixer thread that produces and writes directly to PortAudio
    private readonly Thread _mixerThread;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Lock _mixerLock = new();

    // Atomic state
    private volatile bool _threadShouldQuit;
    private readonly int _sampleRateHz = DefaultSampleRateHz;
    private readonly int _blocksize = DefaultBlocksize;

    // Master volume (atomic via Interlocked operations)
    private AudioFrame _masterGain = new(Minus6db, Minus6db);

    // Controls whether audio is playing, muted, or disabled
    private MixerState _state = MixerState.On;
    private bool _isManuallyMuted = false;

    // Tracks whether audio output stream has been started.
    // Mirrors DOSBox behavior: "An opened audio device starts out paused"
    // Reference: DOSBox mixer.cpp - SDL_PauseAudioDevice(mixer.sdl_device, Unpause)
    private bool _audioStreamStarted = false;

    private CrossfeedPreset _crossfeedPreset = CrossfeedPreset.None;
    private ReverbPreset _reverbPreset = ReverbPreset.None;
    private ChorusPreset _chorusPreset = ChorusPreset.None;

    // Output buffers - matches DOSBox mixer output_buffer
    private readonly AudioFrameBuffer _outputBuffer = new(0);
    private readonly AudioFrameBuffer _reverbAuxBuffer = new(0);
    private readonly AudioFrameBuffer _chorusAuxBuffer = new(0);

    private bool _doCompressor = false;
    private readonly Compressor _compressor = new();

    private float _peakLeft = 0.0f;
    private float _peakRight = 0.0f;
    private const float PeakDecayCoeff = 0.995f; // Slow decay for peak tracking

    // Reverb state - MVerb professional algorithmic reverb
    private bool _doReverb = false;
    private readonly MVerb _mverb = new();
    private float _reverbSynthSendLevel = 0.0f;
    private float _reverbDigitalSendLevel = 0.0f;

    // Chorus state - TAL-Chorus professional modulated chorus
    private bool _doChorus = false;
    private readonly ChorusEngine _chorusEngine;
    private float _chorusSynthSendLevel = 0.0f;
    private float _chorusDigitalSendLevel = 0.0f;

    // Crossfeed state - stereo mixing for headphone spatialization
    private bool _doCrossfeed = false;
    private float _crossfeedGlobalStrength = 0.0f; // Varies by preset: Light=0.20f, Normal=0.40f, Strong=0.60f

    // Used on reverb input and master output
    private readonly HighPassFilter[] _reverbHighPassFilter;
    private readonly HighPassFilter[] _masterHighPassFilter;
    private const int HighPassFilterOrder = 2; // 2nd-order Butterworth
    private const float ReverbHighPassCutoffHz = 120.0f; // Low-frequency cutoff for reverb
    private const float MasterHighPassCutoffHz = 3.0f; // Very low DC-blocking filter for master

    // Final output queue is not used; mixer writes directly

    private bool _disposed;

    public Mixer(ILoggerService loggerService, AudioEngine audioEngine) {
        _loggerService = loggerService ?? throw new ArgumentNullException(nameof(loggerService));
        _audioPlayerFactory = new AudioPlayerFactory(_loggerService, audioEngine);

        // Create the audio player with our sample rate and blocksize
        _audioPlayer = _audioPlayerFactory.CreatePlayer(_sampleRateHz, _blocksize);

        // Initialize high-pass filters (2 channels - left and right)
        _reverbHighPassFilter = new HighPassFilter[2];
        _masterHighPassFilter = new HighPassFilter[2];

        for (int i = 0; i < 2; i++) {
            _reverbHighPassFilter[i] = new HighPassFilter(HighPassFilterOrder);
            _reverbHighPassFilter[i].Setup(HighPassFilterOrder, _sampleRateHz, ReverbHighPassCutoffHz);

            _masterHighPassFilter[i] = new HighPassFilter(HighPassFilterOrder);
            _masterHighPassFilter[i].Setup(HighPassFilterOrder, _sampleRateHz, MasterHighPassCutoffHz);
        }

        // Initialize MVerb with default parameters
        _mverb.SetSampleRate(_sampleRateHz);

        // Initialize ChorusEngine with default sample rate
        _chorusEngine = new ChorusEngine(_sampleRateHz);

        // Configure chorus: Chorus1 enabled, Chorus2 disabled (matches DOSBox)
        // See DOSBox mixer.cpp lines 146-147
        _chorusEngine.SetEnablesChorus(isChorus1Enabled: true, isChorus2Enabled: false);

        // Initialize compressor with default parameters
        InitCompressor(compressorEnabled: true);

        // Start mixer thread (produces frames and writes to PortAudio directly)
        _mixerThread = new Thread(MixerThreadLoop) {
            Name = "Spice86-Mixer",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };
        _mixerThread.Start();

        _loggerService.Information("MIXER: Initialized stereo {SampleRate} Hz audio with {BlockSize} sample frame buffer",
            _sampleRateHz, _blocksize);
    }

    /// <summary>
    /// Gets the current mixer sample rate.
    /// </summary>
    public int SampleRateHz => _sampleRateHz;

    /// <summary>
    /// Gets the mixer sample rate.
    /// </summary>
    public int GetSampleRate() {
        return _sampleRateHz;
    }

    /// <summary>
    /// Gets the current blocksize.
    /// </summary>
    public int Blocksize => _blocksize;

    /// <summary>
    /// Gets the prebuffer time in milliseconds.
    /// </summary>
    public int GetPreBufferMs() {
        // For now return a constant; DOSBox calculates based on buffer size
        return MaxPrebufferMs / 2; // Conservative default
    }

    /// <summary>
    /// Locks the mixer thread to prevent mixing during critical operations.
    /// Note: DOSBox also stops device queues; we just lock the mixer.
    /// Use within a using statement or with UnlockMixerThread().
    /// </summary>
    public void LockMixerThread() {
        _mixerLock.Enter();
    }

    /// <summary>
    /// Unlocks the mixer thread after critical operations complete.
    /// </summary>
    public void UnlockMixerThread() {
        _mixerLock.Exit();
    }

    /// <summary>
    /// Gets or sets the master volume gain.
    /// </summary>
    public AudioFrame MasterGain {
        get {
            lock (_mixerLock) {
                return _masterGain;
            }
        }
        set {
            lock (_mixerLock) {
                _masterGain = value;
            }
        }
    }

    /// <summary>
    /// Gets the current master volume gain.
    /// DOSBox doesn't have a getter function; this provides read access to master_gain.
    /// </summary>
    /// <returns>The current master gain as an AudioFrame.</returns>
    public AudioFrame GetMasterVolume() {
        lock (_mixerLock) {
            return _masterGain;
        }
    }

    /// <summary>
    /// Sets the master volume gain atomically.
    /// </summary>
    /// <param name="gain">The new master gain to apply.</param>
    public void SetMasterVolume(AudioFrame gain) {
        lock (_mixerLock) {
            _masterGain = gain;
        }
    }

    /// <summary>
    /// Mutes audio output while keeping the audio device active.
    /// </summary>
    public void Mute() {
        lock (_mixerLock) {
            if (_state == MixerState.On) {
                _state = MixerState.Muted;
                _isManuallyMuted = true;
                _loggerService.Information("MIXER: Muted audio output");
            }
        }
    }

    /// <summary>
    /// Unmutes audio output, resuming playback.
    /// </summary>
    public void Unmute() {
        lock (_mixerLock) {
            if (_state == MixerState.Muted) {
                _state = MixerState.On;
                _isManuallyMuted = false;
                _loggerService.Information("MIXER: Unmuted audio output");
            }
        }
    }

    /// <summary>
    /// Returns whether audio has been manually muted by the user.
    /// </summary>
    /// <returns>True if audio is manually muted, false otherwise.</returns>
    public bool IsManuallyMuted() {
        lock (_mixerLock) {
            return _isManuallyMuted;
        }
    }

    /// <summary>
    /// Gets the current crossfeed preset.
    /// DOSBox doesn't have a getter; this provides read access to mixer.crossfeed.preset
    /// </summary>
    public CrossfeedPreset GetCrossfeedPreset() {
        lock (_mixerLock) {
            return _crossfeedPreset;
        }
    }

    /// <summary>
    /// Sets the crossfeed preset and configures the effect.
    /// </summary>
    public void SetCrossfeedPreset(CrossfeedPreset preset) {
        lock (_mixerLock) {
            // Unchanged?
            if (_crossfeedPreset == preset) {
                return;
            }

            // Set new preset and strength values matching DOSBox mixer.cpp:434-436
            _crossfeedPreset = preset;
            switch (preset) {
                case CrossfeedPreset.None:
                    _crossfeedGlobalStrength = 0.0f;
                    break;
                case CrossfeedPreset.Light:
                    _crossfeedGlobalStrength = 0.20f; // DOSBox: 0.20f
                    break;
                case CrossfeedPreset.Normal:
                    _crossfeedGlobalStrength = 0.40f; // DOSBox: 0.40f
                    break;
                case CrossfeedPreset.Strong:
                    _crossfeedGlobalStrength = 0.60f; // DOSBox: 0.60f
                    break;
            }

            // Configure the channels
            _doCrossfeed = (preset != CrossfeedPreset.None);

            SetGlobalCrossfeed();

            // Log the change
            if (_doCrossfeed) {
                _loggerService.Information("MIXER: Crossfeed enabled ('{Preset}' preset)", preset);
            } else {
                _loggerService.Information("MIXER: Crossfeed disabled");
            }
        }
    }

    /// <summary>
    /// Applies global crossfeed settings to all channels.
    /// </summary>
    private void SetGlobalCrossfeed() {
        // Apply preset-specific crossfeed strength to stereo channels
        // DOSBox applies to OPL and CMS channels; we apply to all stereo channels
        float globalStrength = _doCrossfeed ? _crossfeedGlobalStrength : 0.0f;

        foreach (MixerChannel channel in _channels.Values) {
            if (channel.HasFeature(ChannelFeature.Stereo)) {
                channel.SetCrossfeedStrength(globalStrength);
            } else {
                channel.SetCrossfeedStrength(0.0f);
            }
        }
    }

    /// <summary>
    /// Gets the current reverb preset.
    /// DOSBox doesn't have a getter; this provides read access to mixer.reverb.preset
    /// </summary>
    public ReverbPreset GetReverbPreset() {
        lock (_mixerLock) {
            return _reverbPreset;
        }
    }

    /// <summary>
    /// Sets the reverb preset and configures the effect.
    /// </summary>
    public void SetReverbPreset(ReverbPreset preset) {
        lock (_mixerLock) {
            if (_reverbPreset == preset) {
                return;
            }

            _reverbPreset = preset;

            // Parameters: PREDLY EARLY  SIZE   DENSITY BW_FREQ DECAY  DAMP_LV SYN_LV DIG_LV HIPASS_HZ
            switch (preset) {
                case ReverbPreset.Tiny:
                    SetupMVerb(predelay: 0.00f, earlyMix: 1.00f, size: 0.05f, density: 0.50f,
                              bandwidthFreq: 0.50f, decay: 0.00f, dampingFreq: 1.00f,
                              synthLevel: 0.65f, digitalLevel: 0.65f, highpassHz: 200.0f);
                    break;
                case ReverbPreset.Small:
                    SetupMVerb(predelay: 0.00f, earlyMix: 1.00f, size: 0.17f, density: 0.42f,
                              bandwidthFreq: 0.50f, decay: 0.50f, dampingFreq: 0.70f,
                              synthLevel: 0.40f, digitalLevel: 0.08f, highpassHz: 200.0f);
                    break;
                case ReverbPreset.Medium:
                    SetupMVerb(predelay: 0.00f, earlyMix: 0.75f, size: 0.50f, density: 0.50f,
                              bandwidthFreq: 0.95f, decay: 0.42f, dampingFreq: 0.21f,
                              synthLevel: 0.54f, digitalLevel: 0.07f, highpassHz: 170.0f);
                    break;
                case ReverbPreset.Large:
                    SetupMVerb(predelay: 0.00f, earlyMix: 0.75f, size: 0.75f, density: 0.50f,
                              bandwidthFreq: 0.95f, decay: 0.52f, dampingFreq: 0.21f,
                              synthLevel: 0.70f, digitalLevel: 0.05f, highpassHz: 140.0f);
                    break;
                case ReverbPreset.Huge:
                    SetupMVerb(predelay: 0.00f, earlyMix: 0.75f, size: 0.75f, density: 0.50f,
                              bandwidthFreq: 0.95f, decay: 0.52f, dampingFreq: 0.21f,
                              synthLevel: 0.85f, digitalLevel: 0.05f, highpassHz: 140.0f);
                    break;
                case ReverbPreset.None:
                    break;
            }

            _doReverb = preset != ReverbPreset.None;

            if (_doReverb) {
                _loggerService.Information("MIXER: Reverb enabled ('{Preset}' preset)", preset);
            } else {
                _loggerService.Information("MIXER: Reverb disabled");
            }

            SetGlobalReverb();
        }
    }

    /// <summary>
    /// Configures MVerb reverb parameters.
    /// </summary>
    private void SetupMVerb(float predelay, float earlyMix, float size, float density,
                           float bandwidthFreq, float decay, float dampingFreq,
                           float synthLevel, float digitalLevel, float highpassHz) {
        _reverbSynthSendLevel = synthLevel;
        _reverbDigitalSendLevel = digitalLevel;

        _mverb.SetParameter(MVerb.Parameter.Predelay, predelay);
        _mverb.SetParameter(MVerb.Parameter.EarlyMix, earlyMix);
        _mverb.SetParameter(MVerb.Parameter.Size, size);
        _mverb.SetParameter(MVerb.Parameter.Density, density);
        _mverb.SetParameter(MVerb.Parameter.BandwidthFreq, bandwidthFreq);
        _mverb.SetParameter(MVerb.Parameter.Decay, decay);
        _mverb.SetParameter(MVerb.Parameter.DampingFreq, dampingFreq);

        // Always max gain (no attenuation)
        _mverb.SetParameter(MVerb.Parameter.Gain, 1.0f);

        // Always 100% wet output signal
        _mverb.SetParameter(MVerb.Parameter.Mix, 1.0f);

        _mverb.SetSampleRate(_sampleRateHz);

        // Update reverb high-pass filter cutoff
        for (int i = 0; i < 2; i++) {
            _reverbHighPassFilter[i].Setup(HighPassFilterOrder, _sampleRateHz, highpassHz);
        }
    }

    /// <summary>
    /// Applies global reverb settings to all channels.
    /// </summary>
    private void SetGlobalReverb() {
        foreach (MixerChannel channel in _channels.Values) {
            if (!_doReverb || !channel.HasFeature(ChannelFeature.ReverbSend)) {
                channel.SetReverbLevel(0.0f);
            } else if (channel.HasFeature(ChannelFeature.Synthesizer)) {
                // Use configured synth send level from preset
                channel.SetReverbLevel(_reverbSynthSendLevel);
            } else if (channel.HasFeature(ChannelFeature.DigitalAudio)) {
                // Use configured digital send level from preset
                channel.SetReverbLevel(_reverbDigitalSendLevel);
            }
        }
    }

    /// <summary>
    /// Gets the current chorus preset.
    /// DOSBox doesn't have a getter; this provides read access to mixer.chorus.preset
    /// </summary>
    public ChorusPreset GetChorusPreset() {
        lock (_mixerLock) {
            return _chorusPreset;
        }
    }

    /// <summary>
    /// Sets the chorus preset and configures the effect.
    /// </summary>
    public void SetChorusPreset(ChorusPreset preset) {
        lock (_mixerLock) {
            if (_chorusPreset == preset) {
                return;
            }

            _chorusPreset = preset;

            // Configure chorus with DOSBox preset values (mixer.cpp:633-636)
            // Preset values: Light (0.33, 0.00), Normal (0.54, 0.00), Strong (0.75, 0.00)
            // Format: (synth_level, digital_level)
            switch (preset) {
                case ChorusPreset.Light:
                    _chorusSynthSendLevel = 0.33f;
                    _chorusDigitalSendLevel = 0.00f;
                    break;
                case ChorusPreset.Normal:
                    _chorusSynthSendLevel = 0.54f;
                    _chorusDigitalSendLevel = 0.00f;
                    break;
                case ChorusPreset.Strong:
                    _chorusSynthSendLevel = 0.75f;
                    _chorusDigitalSendLevel = 0.00f;
                    break;
                case ChorusPreset.None:
                    _chorusSynthSendLevel = 0.0f;
                    _chorusDigitalSendLevel = 0.0f;
                    break;
            }

            // Update ChorusEngine configuration (matches DOSBox mixer.cpp:641-647)
            _chorusEngine.SetSampleRate(_sampleRateHz);
            _chorusEngine.SetEnablesChorus(isChorus1Enabled: true, isChorus2Enabled: false);

            _doChorus = preset != ChorusPreset.None;

            if (_doChorus) {
                _loggerService.Information("MIXER: Chorus enabled ('{Preset}' preset)", preset);
            } else {
                _loggerService.Information("MIXER: Chorus disabled");
            }

            SetGlobalChorus();
        }
    }

    /// <summary>
    /// Applies global chorus settings to all channels.
    /// </summary>
    private void SetGlobalChorus() {
        foreach (MixerChannel channel in _channels.Values) {
            if (!_doChorus || !channel.HasFeature(ChannelFeature.ChorusSend)) {
                channel.SetChorusLevel(0.0f);
            } else if (channel.HasFeature(ChannelFeature.Synthesizer)) {
                // Use configured synth send level from preset
                channel.SetChorusLevel(_chorusSynthSendLevel);
            } else if (channel.HasFeature(ChannelFeature.DigitalAudio)) {
                // Use configured digital send level from preset
                channel.SetChorusLevel(_chorusDigitalSendLevel);
            }
        }
    }

    /// <summary>
    /// Adds a channel to the mixer.
    /// </summary>
    public MixerChannel AddChannel(
        Action<int> handler,
        int sampleRateHz,
        string name,
        HashSet<ChannelFeature> features) {

        if (sampleRateHz == 0) {
            sampleRateHz = _sampleRateHz;
        }

        MixerChannel channel = new(handler, name, features, _loggerService);
        channel.SetMixerSampleRate(_sampleRateHz); // Tell channel about mixer rate
        channel.SetSampleRate(sampleRateHz);
        channel.SetAppVolume(new AudioFrame(1.0f, 1.0f));
        channel.SetUserVolume(new AudioFrame(1.0f, 1.0f));

        int channelRate = channel.GetSampleRate();
        if (channelRate == _sampleRateHz) {
            _loggerService.Information("{ChannelName}: Operating at {Rate} Hz without resampling",
                name, channelRate);
        } else {
            _loggerService.Information("{ChannelName}: Operating at {Rate} Hz and {Direction} to the output rate",
                name, channelRate,
                channelRate > _sampleRateHz ? "downsampling" : "upsampling");
        }

        // Add to channels registry
        if (!_channels.TryAdd(name, channel)) {
            // Replace existing
            _channels[name] = channel;
        }

        // Set default state
        channel.Enable(false);
        channel.SetChannelMap(new StereoLine { Left = LineIndex.Left, Right = LineIndex.Right });

        return channel;
    }

    /// <summary>
    /// Finds a channel by name.
    /// </summary>
    public MixerChannel? FindChannel(string name) {
        _channels.TryGetValue(name, out MixerChannel? channel);
        return channel;
    }

    /// <summary>
    /// Removes a channel from the mixer.
    /// </summary>
    public void DeregisterChannel(string name) {
        if (_channels.TryRemove(name, out MixerChannel? channel)) {
            channel.Enable(false);
            _loggerService.Debug("MIXER: Deregistered channel {ChannelName}", name);
        }
    }

    /// <summary>
    /// Gets all registered mixer channels.
    /// </summary>
    public IEnumerable<MixerChannel> GetAllChannels() {
        return _channels.Values;
    }

    /// <summary>
    /// Main mixer thread loop.
    /// </summary>
    private void MixerThreadLoop() {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
            _loggerService.Debug("MIXER: Mixer thread started. sampleRate={SampleRateHz}, blocksize={Blocksize}", _sampleRateHz, _blocksize);
        }

        CancellationToken token = _cancellationTokenSource.Token;
        while (!_threadShouldQuit && !token.IsCancellationRequested) {
            int framesRequested = _blocksize;

            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                _loggerService.Verbose("MIXER: Begin mix cycle frames={FramesRequested} channels={ChannelCount}", framesRequested, _channels.Count);
            }

            float[] rentedBuffer = Array.Empty<float>();
            try {
                lock (_mixerLock) {
                    bool anyChannelContributed = MixSamples(framesRequested);

                    // Start audio stream when first channel produces audio.
                    // Mirrors DOSBox: "SDL starts out paused so unpause it when we first set the mixer state"
                    // Reference: DOSBox mixer.cpp - SDL_PauseAudioDevice(mixer.sdl_device, Unpause)
                    if (!_audioStreamStarted) {
                        if (!anyChannelContributed) {
                            // No channels active yet - skip output until audio is ready
                            continue;
                        }
                        _audioPlayer.Start();
                        _audioStreamStarted = true;
                        _loggerService.Information("MIXER: Audio stream started (first channel activated)");
                    }

                    if (_state == MixerState.Muted) {
                        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                            _loggerService.Verbose("MIXER: Skipping audio output (muted)");
                        }
                        continue;
                    }

                    int framesToWrite = _outputBuffer.Count;
                    if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                        _loggerService.Verbose("MIXER: Mixed frames={Frames}", framesToWrite);
                    }

                    if (framesToWrite == 0) {
                        continue;
                    }

                    rentedBuffer = System.Buffers.ArrayPool<float>.Shared.Rent(framesToWrite * 2);
                    Span<float> interleavedBuffer = rentedBuffer.AsSpan(0, framesToWrite * 2);
                    const float normalizeFactor = 1.0f / 32768.0f;
                    Span<AudioFrame> outputFrames = _outputBuffer.AsSpan(0, framesToWrite);
                    for (int i = 0; i < framesToWrite; i++) {
                        int offset = i * 2;
                        interleavedBuffer[offset] = outputFrames[i].Left * normalizeFactor;
                        interleavedBuffer[offset + 1] = outputFrames[i].Right * normalizeFactor;
                    }

                    _audioPlayer.WriteData(interleavedBuffer);

                    if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                        _loggerService.Verbose("MIXER: Wrote frames to PortAudio frames={Frames}", framesToWrite);
                    }
                }
            } finally {
                if (rentedBuffer != null) {
                    System.Buffers.ArrayPool<float>.Shared.Return(rentedBuffer);
                }
            }
        }
    }

    // No consumer thread: mixer thread writes directly to the audio backend

    /// <summary>
    /// Mixes samples from all channels into the output buffer.
    /// </summary>
    /// <returns>True if at least one enabled channel contributed audio.</returns>
    private bool MixSamples(int framesRequested) {
        // Clear output buffers
        _outputBuffer.Resize(framesRequested);
        _reverbAuxBuffer.Resize(framesRequested);
        _chorusAuxBuffer.Resize(framesRequested);

        // Initialize with silence
        _outputBuffer.AsSpan().Clear();
        _reverbAuxBuffer.AsSpan().Clear();
        _chorusAuxBuffer.AsSpan().Clear();

        bool anyChannelContributed = false;

        // Mix all enabled channels
        foreach (MixerChannel channel in _channels.Values) {
            if (!channel.IsEnabled) {
                continue;
            }

            anyChannelContributed = true;

            // Request frames from the channel
            channel.Mix(framesRequested);

            // Accumulate channel output into master mix
            int numFrames = Math.Min(framesRequested, channel.AudioFrames.Count);
            Span<AudioFrame> channelFrames = channel.AudioFrames.AsSpan(0, numFrames);
            Span<AudioFrame> outputFrames = _outputBuffer.AsSpan(0, numFrames);
            Span<AudioFrame> reverbFrames = _reverbAuxBuffer.AsSpan(0, numFrames);
            Span<AudioFrame> chorusFrames = _chorusAuxBuffer.AsSpan(0, numFrames);
            for (int i = 0; i < numFrames; i++) {
                AudioFrame channelFrame = channelFrames[i];

                // Apply sleep/wake fade-out or signal detection if enabled
                channelFrame = channel.MaybeFadeOrListen(channelFrame);

                // Add to master output using operator
                outputFrames[i] = outputFrames[i] + channelFrame;

                if (_doReverb && channel.DoReverbSend) {
                    reverbFrames[i] = reverbFrames[i] + (channelFrame * channel.ReverbSendGain);
                }

                if (_doChorus && channel.DoChorusSend) {
                    chorusFrames[i] = chorusFrames[i] + (channelFrame * channel.ChorusSendGain);
                }
            }

            // Remove consumed frames from channel
            if (numFrames > 0) {
                channel.AudioFrames.RemoveRange(0, numFrames);
                if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                    _loggerService.Verbose("MIXER: Channel {Channel} provided frames={Frames}", channel.GetName(), numFrames);
                }
            }

            channel.MaybeSleep();
        }

        // Apply master gain using operator
        AudioFrame masterGainSnapshot = _masterGain;
        Span<AudioFrame> outputAll = _outputBuffer.AsSpan();
        for (int i = 0; i < outputAll.Length; i++) {
            outputAll[i] = outputAll[i] * masterGainSnapshot;
        }

        if (_doReverb) {
            // Apply high-pass filter to reverb aux buffer before reverb processing
            Span<AudioFrame> reverbAll = _reverbAuxBuffer.AsSpan();
            for (int i = 0; i < reverbAll.Length; i++) {
                AudioFrame frame = reverbAll[i];
                frame = new AudioFrame(
                    _reverbHighPassFilter[0].Filter(frame.Left),
                    _reverbHighPassFilter[1].Filter(frame.Right)
                );
                reverbAll[i] = frame;
            }

            ApplyReverb();
        }

        if (_doChorus) {
            ApplyChorus();
        }

        // Apply crossfeed if enabled (uses per-channel strength set by SetGlobalCrossfeed)
        if (_doCrossfeed) {
            ApplyCrossfeed();
        }

        // This is a DC-blocking filter to prevent low-frequency buildup
        for (int i = 0; i < _outputBuffer.Count; i++) {
            AudioFrame frame = _outputBuffer[i];
            frame = new AudioFrame(
                _masterHighPassFilter[0].Filter(frame.Left),
                _masterHighPassFilter[1].Filter(frame.Right)
            );
            _outputBuffer[i] = frame;
        }

        if (_doCompressor) {
            ApplyCompressor();
        }

        ApplyMasterNormalization();

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("MIXER: Post-normalization peaks L={PeakLeft:0.0000} R={PeakRight:0.0000}", _peakLeft, _peakRight);
        }

        return anyChannelContributed;
    }

    /// <summary>
    /// Initializes the master compressor with professional RMS-based configuration.
    /// </summary>
    /// <param name="compressorEnabled">Whether to enable the compressor</param>
    private void InitCompressor(bool compressorEnabled) {
        _doCompressor = compressorEnabled;
        if (!_doCompressor) {
            _loggerService.Information("MIXER: Master compressor disabled");
            return;
        }

        LockMixerThread();

        const float ZeroDbfsSampleValue = 32767.0f; // Max16BitSampleValue = INT16_MAX
        const float ThresholdDb = -6.0f;
        const float Ratio = 3.0f;
        const float AttackTimeMs = 0.01f;
        const float ReleaseTimeMs = 5000.0f;
        const float RmsWindowMs = 10.0f;

        _compressor.Configure(
            _sampleRateHz,
            ZeroDbfsSampleValue,
            ThresholdDb,
            Ratio,
            AttackTimeMs,
            ReleaseTimeMs,
            RmsWindowMs
        );

        UnlockMixerThread();

        _loggerService.Information("MIXER: Master compressor enabled");
    }

    /// <summary>
    /// Applies professional RMS-based compressor to reduce dynamic range.
    /// </summary>
    private void ApplyCompressor() {
        // Process each frame through the compressor
        for (int i = 0; i < _outputBuffer.Count; i++) {
            _outputBuffer[i] = _compressor.Process(_outputBuffer[i]);
        }
    }

    /// <summary>
    /// Applies master normalization to prevent clipping and maintain consistent levels.
    /// </summary>
    private void ApplyMasterNormalization() {
        // Track peaks for adaptive gain
        for (int i = 0; i < _outputBuffer.Count; i++) {
            AudioFrame frame = _outputBuffer[i];

            // Update peak trackers
            float absLeft = Math.Abs(frame.Left);
            float absRight = Math.Abs(frame.Right);

            if (absLeft > _peakLeft) {
                _peakLeft = absLeft;
            } else {
                _peakLeft *= PeakDecayCoeff;
            }

            if (absRight > _peakRight) {
                _peakRight = absRight;
            } else {
                _peakRight *= PeakDecayCoeff;
            }

            // Apply soft clipping to both channels
            float left = ApplySoftClipping(frame.Left);
            float right = ApplySoftClipping(frame.Right);

            _outputBuffer[i] = new AudioFrame(left, right);
        }
    }

    /// <summary>
    /// Applies soft-knee limiting to a single sample to prevent harsh clipping.
    /// Similar to DOSBox normalize_sample() soft-limiting approach
    /// </summary>
    private static float ApplySoftClipping(float sample) {
        const float softClipThreshold = 32000.0f;
        const float hardLimit = 32767.0f;

        if (Math.Abs(sample) > softClipThreshold) {
            float sign = Math.Sign(sample);
            float excess = Math.Abs(sample) - softClipThreshold;
            float softened = softClipThreshold + excess * 0.5f; // Reduce overshoot by half
            return Math.Clamp(sign * softened, -hardLimit, hardLimit);
        }

        return sample;
    }

    /// <summary>
    /// Applies MVerb professional algorithmic reverb effect.
    /// </summary>
    private void ApplyReverb() {
        // Prepare buffers for MVerb processing
        // MVerb operates on non-interleaved sample streams (separate L/R arrays)
        int frameCount = _reverbAuxBuffer.Count;
        float[] leftIn = new float[frameCount];
        float[] rightIn = new float[frameCount];
        float[] leftOut = new float[frameCount];
        float[] rightOut = new float[frameCount];

        // Extract left and right channels from reverb aux buffer
        // Apply high-pass filter to reverb input (removes low-frequency buildup)
        for (int i = 0; i < frameCount; i++) {
            AudioFrame frame = _reverbAuxBuffer[i];
            leftIn[i] = _reverbHighPassFilter[0].Filter(frame.Left);
            rightIn[i] = _reverbHighPassFilter[1].Filter(frame.Right);
        }

        // Process through MVerb (FDN reverb algorithm)
        _mverb.Process(leftIn, rightIn, leftOut, rightOut, frameCount);

        // Mix reverb output with main output buffer
        for (int i = 0; i < frameCount; i++) {
            _outputBuffer[i] = new AudioFrame(
                _outputBuffer[i].Left + leftOut[i],
                _outputBuffer[i].Right + rightOut[i]
            );
        }
    }

    /// <summary>
    /// Applies TAL-Chorus effect to the chorus aux buffer and mixes to output.
    /// </summary>
    /// <remarks>
    /// Processing flow:
    /// 1. For each frame in chorus aux buffer (contains sum of channel chorus sends)
    /// 2. Process through ChorusEngine (modulated delay with LFO)
    /// 3. Add processed chorus output to master output buffer
    /// 
    /// The ChorusEngine processes samples in-place, modifying them with the chorus effect.
    /// </remarks>
    private void ApplyChorus() {
        // Apply chorus effect to the chorus aux buffer, then mix to master output
        for (int i = 0; i < _chorusAuxBuffer.Count; i++) {
            float left = _chorusAuxBuffer[i].Left;
            float right = _chorusAuxBuffer[i].Right;

            // Process through TAL-Chorus engine (in-place modification)
            _chorusEngine.Process(ref left, ref right);

            // Add processed chorus to output buffer
            _outputBuffer[i] = new AudioFrame(
                _outputBuffer[i].Left + left,
                _outputBuffer[i].Right + right
            );
        }
    }

    /// <summary>
    /// Applies crossfeed effect for headphone spatialization.
    /// Note: In DOSBox, crossfeed is applied per-channel in MixerChannel::ApplyCrossfeed.
    /// This is a master-level crossfeed for any remaining unmixed channels.
    /// </summary>
    private void ApplyCrossfeed() {
        for (int i = 0; i < _outputBuffer.Count; i++) {
            AudioFrame frame = _outputBuffer[i];

            // Mix some of each channel into the opposite channel
            // This simulates speaker crosstalk for headphone listening
            float newLeft = frame.Left + frame.Right * _crossfeedGlobalStrength;
            float newRight = frame.Right + frame.Left * _crossfeedGlobalStrength;

            _outputBuffer[i] = new AudioFrame(newLeft, newRight);
        }
    }

    /// <summary>
    /// Closes the audio device and stops all channels.
    /// </summary>
    public void CloseAudioDevice() {
        lock (_mixerLock) {
            // Stop mixer thread
            if (_mixerThread.IsAlive) {
                _threadShouldQuit = true;
                _cancellationTokenSource.Cancel();
                _mixerThread.Join(TimeSpan.FromSeconds(5));
            }

            // Disable all channels
            foreach (MixerChannel channel in _channels.Values) {
                channel.Enable(false);
            }

            // Close audio player
            _audioPlayer.Dispose();

            _loggerService.Information("MIXER: Closed audio device");
        }
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }

        _loggerService.Debug("MIXER: Disposing mixer");

        _threadShouldQuit = true;
        _cancellationTokenSource.Cancel();

        if (_mixerThread.IsAlive) {
            _mixerThread.Join(TimeSpan.FromSeconds(1));
        }

        _cancellationTokenSource.Dispose();
        _audioPlayer.Dispose();
        //_chorusEngine.Dispose();

        _disposed = true;
    }
}
