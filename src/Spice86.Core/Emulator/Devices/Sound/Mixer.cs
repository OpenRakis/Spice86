namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Backend.Audio;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

using AudioFrame = Spice86.Libs.Sound.Common.AudioFrame;
using HighPassFilter = Spice86.Libs.Sound.Filters.IirFilters.Filters.Butterworth.HighPass;

/// <summary>
/// Central audio mixer that runs in its own thread and produces final mixed output.
/// </summary>
public sealed class Mixer : IDisposable {
    private const int DefaultSampleRateHz = 48000;
    private const int DefaultBlocksize = 1024;
    private const int DefaultPrebufferMs = 25;

    // This shows up nicely as 50% and -6.00 dB in the MIXER command's output
    private const float Minus6db = 0.501f;

    private readonly ILoggerService _loggerService;
    private readonly AudioPlayerFactory _audioPlayerFactory;
    private readonly AudioPlayer _audioPlayer;
    private readonly IPauseHandler _pauseHandler;

    // Channels registry - matches DOSBox mixer.channels
    private readonly Dictionary<string, MixerChannel> _channels = new();
    private readonly Dictionary<string, MixerChannelSettings> _channelSettingsCache = new();

    // Mixer thread that produces audio and sends to the audio backend
    private readonly Thread _mixerThread;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Lock _mixerLock = new();

    // Atomic state
    private volatile bool _threadShouldQuit;
    private int _sampleRateHz = DefaultSampleRateHz;
    private int _blocksize = DefaultBlocksize;
    private readonly int _prebufferMs = DefaultPrebufferMs;

    // Master volume (atomic via Interlocked operations)
    private AudioFrame _masterGain = new(Minus6db, Minus6db);

    // Controls whether audio is playing, muted, or disabled
    private MixerState _state = MixerState.On;
    private bool _isManuallyMuted = false;

    private CrossfeedPreset _crossfeedPreset = CrossfeedPreset.None;
    private ReverbPreset _reverbPreset = ReverbPreset.None;
    private ChorusPreset _chorusPreset = ChorusPreset.None;

    // Output buffers - matches DOSBox mixer output_buffer
    private readonly AudioFrameBuffer _outputBuffer = new(0);
    private readonly AudioFrameBuffer _reverbAuxBuffer = new(0);
    private readonly AudioFrameBuffer _chorusAuxBuffer = new(0);

    private bool _doCompressor = false;
    private readonly Compressor _compressor = new();

    // Reverb state - MVerb professional algorithmic reverb
    private bool _doReverb = false;
    private readonly MVerb _mverb = new();
    private float _reverbSynthSendLevel = 0.0f;
    private float _reverbDigitalSendLevel = 0.0f;

    // Pre-allocated reverb processing buffers (avoids per-tick GC allocations)
    private float[] _reverbLeftIn = new float[DefaultBlocksize];
    private float[] _reverbRightIn = new float[DefaultBlocksize];
    private float[] _reverbLeftOut = new float[DefaultBlocksize];
    private float[] _reverbRightOut = new float[DefaultBlocksize];

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
    private const float MasterHighPassCutoffHz = 20.0f; // DOSBox: HighpassCutoffFreqHz = 20.0

    // Final output queue is not used; mixer writes directly

    private bool _disposed;

    /// <summary>
    /// Creates a new Mixer instance.
    /// </summary>
    /// <param name="loggerService">Logger service.</param>
    /// <param name="audioEngine">Audio engine to use.</param>
    /// <param name="pauseHandler">Pause handler to mute audio when emulator is paused.</param>
    public Mixer(ILoggerService loggerService, AudioEngine audioEngine, IPauseHandler pauseHandler) {
        _loggerService = loggerService ?? throw new ArgumentNullException(nameof(loggerService));
        _pauseHandler = pauseHandler ?? throw new ArgumentNullException(nameof(pauseHandler));
        _audioPlayerFactory = new AudioPlayerFactory(_loggerService, audioEngine);

        // Create the audio player with our sample rate and blocksize
        _audioPlayer = _audioPlayerFactory.CreatePlayer(_sampleRateHz, _blocksize, _prebufferMs);
        if (_audioPlayer.Format.SampleRate > 0) {
            _sampleRateHz = _audioPlayer.Format.SampleRate;
        }
        if (_audioPlayer.BufferFrames > 0) {
            _blocksize = _audioPlayer.BufferFrames;
        }

        // Initialize high-pass filters (2 channels - left and right)
        _reverbHighPassFilter = new HighPassFilter[2];
        _masterHighPassFilter = new HighPassFilter[2];
        const float DefaultReverbHighPassHz = 200.0f;

        for (int i = 0; i < 2; i++) {
            _reverbHighPassFilter[i] = new HighPassFilter(HighPassFilterOrder);
            _reverbHighPassFilter[i].Setup(HighPassFilterOrder, _sampleRateHz, DefaultReverbHighPassHz);

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

        // Start mixer thread (produces frames and sends to audio backend)
        _mixerThread = new Thread(MixerThreadLoop) {
            Name = "Spice86-Mixer",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };
        _mixerThread.Start();

        // Subscribe to pause/resume events
        // Reference: DOSBox set_mixer_state (MixerState::Muted / MixerState::On)
        // When emulator pauses, mute mixer; when resumed, unmute.
        _pauseHandler.Pausing += OnEmulatorPausing;
        _pauseHandler.Resumed += OnEmulatorResumed;

        _loggerService.Information("MIXER: Initialized stereo {SampleRate} Hz audio with {BlockSize} sample frame buffer",
            _sampleRateHz, _blocksize);
    }

    /// <summary>
    /// Called when the emulator is about to pause.
    /// Mutes audio output immediately.
    /// </summary>
    private void OnEmulatorPausing() {
        Mute();
    }

    /// <summary>
    /// Called when the emulator resumes from pause.
    /// Unmutes audio output.
    /// </summary>
    private void OnEmulatorResumed() {
        Unmute();
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
        return _prebufferMs;
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
    /// Reference: DOSBox set_mixer_state(MixerState::Muted)
    /// DOSBox clears the output queue to prevent stuttering on un-mute,
    /// then the mixer thread enqueues silence blocks instead of mixed audio.
    /// The SDL callback stays active and keeps draining the queue.
    /// </summary>
    public void Mute() {
        lock (_mixerLock) {
            if (_state == MixerState.On) {
                // Clear out any audio in the queue to avoid a stutter on un-mute
                _audioPlayer.ClearQueuedData();
                _state = MixerState.Muted;
                _isManuallyMuted = true;
                _loggerService.Information("MIXER: Muted audio output");
            }
        }
    }

    /// <summary>
    /// Unmutes audio output, resuming playback.
    /// Reference: DOSBox set_mixer_state(MixerState::On)
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
        // Apply preset-specific crossfeed strength to OPL and CMS channels only
        float globalStrength = _doCrossfeed ? _crossfeedGlobalStrength : 0.0f;
        foreach (MixerChannel channel in _channels.Values) {
            string name = channel.GetName();
            bool applyCrossfeed = name == nameof(Opl) || name == "Cms";
            if (applyCrossfeed && channel.HasFeature(ChannelFeature.Stereo)) {
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

        _mverb.SetParameter((int)MVerb.Parameter.Predelay, predelay);
        _mverb.SetParameter((int)MVerb.Parameter.EarlyMix, earlyMix);
        _mverb.SetParameter((int)MVerb.Parameter.Size, size);
        _mverb.SetParameter((int)MVerb.Parameter.Density, density);
        _mverb.SetParameter((int)MVerb.Parameter.BandwidthFreq, bandwidthFreq);
        _mverb.SetParameter((int)MVerb.Parameter.Decay, decay);
        _mverb.SetParameter((int)MVerb.Parameter.DampingFreq, dampingFreq);

        // Always max gain (no attenuation)
        _mverb.SetParameter((int)MVerb.Parameter.Gain, 1.0f);

        // Always 100% wet output signal
        _mverb.SetParameter((int)MVerb.Parameter.Mix, 1.0f);

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

        if (_channelSettingsCache.TryGetValue(name, out MixerChannelSettings cachedSettings)) {
            channel.SetSettings(cachedSettings);
            ApplyCachedEffectSettings(channel, cachedSettings);
        } else {
            // Set default state
            channel.Enable(false);
            channel.SetUserVolume(new AudioFrame(1.0f, 1.0f));
            channel.SetChannelMap(new StereoLine { Left = LineIndex.Left, Right = LineIndex.Right });
            SetGlobalCrossfeed();
            SetGlobalReverb();
            SetGlobalChorus();
        }

        return channel;
    }

    /// <summary>
    /// Finds a channel by name.
    /// </summary>
    public MixerChannel? FindChannel(string name) {
        _channels.TryGetValue(name, out MixerChannel? channel);
        return channel;
    }

    private void ApplyCachedEffectSettings(MixerChannel channel, MixerChannelSettings settings) {
        if (_doCrossfeed) {
            channel.SetCrossfeedStrength(settings.CrossfeedStrength);
        } else {
            channel.SetCrossfeedStrength(0.0f);
        }

        if (_doReverb) {
            channel.SetReverbLevel(settings.ReverbLevel);
        } else {
            channel.SetReverbLevel(0.0f);
        }

        if (_doChorus) {
            channel.SetChorusLevel(settings.ChorusLevel);
        } else {
            channel.SetChorusLevel(0.0f);
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
    /// Reference: DOSBox mixer.cpp mixer_thread_loop() lines 2606-2700
    /// EXACT MIRROR of DOSBox structure.
    /// </summary>
    private void MixerThreadLoop() {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
            _loggerService.Debug("MIXER: Mixer thread started. sampleRate={SampleRateHz}, blocksize={Blocksize}", _sampleRateHz, _blocksize);
        }

        // Start audio immediately - DOSBox doesn't wait for channel activity
        // Reference: DOSBox SDL audio device starts and the callback runs immediately
        _audioPlayer.Start();
        _loggerService.Information("MIXER: Audio stream started");

        CancellationToken token = _cancellationTokenSource.Token;

        // Silence block for muted state - allocated once, reused
        // Reference: DOSBox mixer_thread_loop muted path creates silence via clear+resize
        float[] silenceBlock = new float[_blocksize * 2]; // stereo interleaved

        while (!_threadShouldQuit && !token.IsCancellationRequested) {
            MixerState state;
            int framesRequested = _blocksize;

            // Reference: DOSBox mixer_thread_loop lines 2607-2636:
            //   std::unique_lock lock(mixer.mutex);
            //   mix_samples(frames_requested);
            //   lock.unlock();
            lock (_mixerLock) {
                state = _state;
                if (state == MixerState.On) {
                    MixSamples(framesRequested);
                }
            } // Unlock mixer for state checks and I/O

            // Handle NoSound state - sleep for expected duration
            // Reference: mixer.cpp lines 2645-2654
            if (state == MixerState.NoSound) {
                // SDL callback is not running. Mixed sound gets discarded.
                // Sleep for the expected duration to simulate playback time.
                double expectedTimeMs = (double)_blocksize / _sampleRateHz * 1000.0;
                HighResolutionWaiter.WaitMilliseconds(expectedTimeMs);
                continue;
            }

            // Enqueue silence to keep the audio callback fed while muted.
            // Prevents clicks/pops on unmute because the audio pipeline stays active.
            // BulkEnqueue blocks until the callback drains enough space.
            if (state == MixerState.Muted) {
                _audioPlayer.WriteData(silenceBlock.AsSpan());
                continue;
            }

            int framesToWrite = _outputBuffer.Count;
            if (framesToWrite == 0) {
                continue;
            }

            // AudioFrame memory layout: [float left][float right] = interleaved stereo.
            // WriteData blocks when the queue is full, providing natural pacing.
            Span<AudioFrame> outputFrames = _outputBuffer.AsSpan(0, framesToWrite);
            Span<float> interleavedBuffer = MemoryMarshal.Cast<AudioFrame, float>(outputFrames);
            _audioPlayer.WriteData(interleavedBuffer);

            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                _loggerService.Verbose("MIXER: Wrote frames to audio backend frames={Frames}", framesToWrite);
            }
        }
    }

    /// <summary>
    /// Mix a certain amount of new sample frames.
    /// Reference: DOSBox mixer.cpp mix_samples() lines 2394-2540
    /// </summary>
    private void MixSamples(int framesRequested) {
        _outputBuffer.Resize(framesRequested);
        _reverbAuxBuffer.Resize(framesRequested);
        _chorusAuxBuffer.Resize(framesRequested);

        _outputBuffer.AsSpan().Clear();
        _reverbAuxBuffer.AsSpan().Clear();
        _chorusAuxBuffer.AsSpan().Clear();

        // Render all channels and accumulate results in the master mixbuffer
        foreach (MixerChannel channel in _channels.Values) {
            channel.Mix(framesRequested);

            int numFrames = Math.Min(_outputBuffer.Count, channel.AudioFrames.Count);

            for (int i = 0; i < numFrames; i++) {
                if (channel.DoSleep) {
                    _outputBuffer[i] = _outputBuffer[i] + channel.MaybeFadeOrListen(channel.AudioFrames[i]);
                } else {
                    _outputBuffer[i] = _outputBuffer[i] + channel.AudioFrames[i];
                }

                if (_doReverb && channel.DoReverbSend) {
                    _reverbAuxBuffer[i] = _reverbAuxBuffer[i] + (channel.AudioFrames[i] * channel.ReverbSendGain);
                }

                if (_doChorus && channel.DoChorusSend) {
                    _chorusAuxBuffer[i] = _chorusAuxBuffer[i] + (channel.AudioFrames[i] * channel.ChorusSendGain);
                }
            }

            channel.AudioFrames.RemoveRange(0, numFrames);

            if (channel.DoSleep) {
                channel.MaybeSleep();
            }
        }

        if (_doReverb) {
            ApplyReverb();
        }

        if (_doChorus) {
            ApplyChorus();
        }

        // Apply high-pass filter to the master output
        for (int i = 0; i < _outputBuffer.Count; i++) {
            AudioFrame frame = _outputBuffer[i];
            _outputBuffer[i] = new AudioFrame(
                _masterHighPassFilter[0].Filter(frame.Left),
                _masterHighPassFilter[1].Filter(frame.Right)
            );
        }

        // Apply master gain
        AudioFrame gain = _masterGain;
        for (int i = 0; i < _outputBuffer.Count; i++) {
            _outputBuffer[i] = _outputBuffer[i] * gain;
        }

        if (_doCompressor) {
            // Apply compressor to the master output as the very last step
            for (int i = 0; i < _outputBuffer.Count; i++) {
                _outputBuffer[i] = _compressor.Process(_outputBuffer[i]);
            }
        }

        // Normalize the final output before sending to SDL
        for (int i = 0; i < _outputBuffer.Count; i++) {
            AudioFrame frame = _outputBuffer[i];
            _outputBuffer[i] = new AudioFrame(
                NormalizeSample(frame.Left),
                NormalizeSample(frame.Right)
            );
        }
    }

    /// <summary>
    /// We use floats in the range of 16 bit integers everywhere.
    /// SDL expects floats to be normalized from 1.0 to -1.0.
    /// Reference: DOSBox mixer.cpp normalize_sample()
    /// </summary>
    private static float NormalizeSample(float sample) {
        return sample / 32768.0f;
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
    /// Applies MVerb professional algorithmic reverb effect.
    /// </summary>
    private void ApplyReverb() {
        // Prepare buffers for MVerb processing
        // MVerb operates on non-interleaved sample streams (separate L/R arrays)
        int frameCount = _reverbAuxBuffer.Count;

        // Ensure pre-allocated buffers are large enough
        if (_reverbLeftIn.Length < frameCount) {
            _reverbLeftIn = new float[frameCount];
            _reverbRightIn = new float[frameCount];
            _reverbLeftOut = new float[frameCount];
            _reverbRightOut = new float[frameCount];
        }

        // Extract left and right channels from reverb aux buffer
        // Apply high-pass filter to reverb input (removes low-frequency buildup)
        for (int i = 0; i < frameCount; i++) {
            AudioFrame frame = _reverbAuxBuffer[i];
            _reverbLeftIn[i] = _reverbHighPassFilter[0].Filter(frame.Left);
            _reverbRightIn[i] = _reverbHighPassFilter[1].Filter(frame.Right);
        }

        // Process through MVerb (FDN reverb algorithm)
        _mverb.Process(
            _reverbLeftIn.AsSpan(0, frameCount),
            _reverbRightIn.AsSpan(0, frameCount),
            _reverbLeftOut.AsSpan(0, frameCount),
            _reverbRightOut.AsSpan(0, frameCount),
            frameCount);

        // Mix reverb output with main output buffer
        for (int i = 0; i < frameCount; i++) {
            _outputBuffer[i] = new AudioFrame(
                _outputBuffer[i].Left + _reverbLeftOut[i],
                _outputBuffer[i].Right + _reverbRightOut[i]
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
    /// Closes the audio device and stops all channels.
    /// </summary>
    private void CloseAudioDevice() {
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

        CloseAudioDevice();

        _loggerService.Debug("MIXER: Disposing mixer");

        // Unsubscribe from pause events
        _pauseHandler.Pausing -= OnEmulatorPausing;
        _pauseHandler.Resumed -= OnEmulatorResumed;

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

    /// <summary>
    /// Generic callback for audio devices that generate audio on the main thread.
    /// These devices produce audio on the main thread and consume on the mixer thread.
    /// This callback is the consumer part.
    /// Reference: DOSBox mixer.h MIXER_PullFromQueueCallback
    /// </summary>
    /// <typeparam name="TDevice">The device type implementing IAudioQueueDevice.</typeparam>
    /// <param name="framesRequested">Number of audio frames requested by the mixer.</param>
    /// <param name="device">The audio device (passed as 'this' from the device).</param>
    public void PullFromQueueCallback<TDevice>(int framesRequested, TDevice device)
        where TDevice : IAudioQueueDevice<float> {
        // Size to 2x blocksize. The mixer callback will request 1x blocksize.
        // This provides a good size to avoid over-runs and stalls.
        // Resize is a fast operation, only setting a variable for max capacity.
        // It does not drop frames or append zeros to the underlying data structure.
        int queueSize = (int)Math.Ceiling(device.Channel.GetFramesPerBlock() * 2.0f);
        device.OutputQueue.Resize(queueSize);

        // Dequeue samples in bulk
        float[] toMix = new float[framesRequested];
        int framesReceived = device.OutputQueue.BulkDequeue(toMix, framesRequested);

        if (framesReceived > 0) {
            device.Channel.AddSamples_mfloat(framesReceived, toMix);
        }

        // Fill any shortfall with silence
        if (framesReceived < framesRequested) {
            device.Channel.AddSilence();
        }
    }

    /// <summary>
    /// Generic callback for audio devices that generate audio on the main thread.
    /// Overload that takes queue and channel directly for simpler use cases.
    /// Reference: DOSBox mixer.h MIXER_PullFromQueueCallback
    /// </summary>
    /// <param name="framesRequested">Number of audio frames requested by the mixer.</param>
    /// <param name="outputQueue">The device's output queue containing audio samples.</param>
    /// <param name="channel">The mixer channel to write audio to.</param>
    public void PullFromQueueCallback(int framesRequested, RWQueue<float> outputQueue, MixerChannel channel) {
        // Size to 2x blocksize. The mixer callback will request 1x blocksize.
        // This provides a good size to avoid over-runs and stalls.
        // Resize is a fast operation, only setting a variable for max capacity.
        // It does not drop frames or append zeros to the underlying data structure.
        int queueSize = (int)Math.Ceiling(channel.GetFramesPerBlock() * 2.0f);
        outputQueue.Resize(queueSize);

        // Dequeue samples in bulk
        float[] toMix = new float[framesRequested];
        int framesReceived = outputQueue.BulkDequeue(toMix, framesRequested);

        if (framesReceived > 0) {
            channel.AddSamples_mfloat(framesReceived, toMix);
        }

        // Fill any shortfall with silence
        if (framesReceived < framesRequested) {
            channel.AddSilence();
        }
    }

    /// <summary>
    /// Generic callback for audio devices that generate stereo AudioFrame output.
    /// These devices produce audio on the main thread and consume on the mixer thread.
    /// This callback is the consumer part.
    /// Reference: DOSBox mixer.h MIXER_PullFromQueueCallback (AudioFrame variant)
    /// </summary>
    /// <typeparam name="TDevice">The device type implementing IAudioQueueDevice&lt;AudioFrame&gt;.</typeparam>
    /// <param name="framesRequested">Number of audio frames requested by the mixer.</param>
    /// <param name="device">The audio device (passed as 'this' from the device).</param>
    /// <param name="framesNeeded">Atomic counter tracking frames needed by the device.</param>
    public void PullFromQueueCallback<TDevice>(int framesRequested, TDevice device, ref int framesNeeded)
        where TDevice : IAudioQueueDevice<AudioFrame> {
        // Calculate shortage and update framesNeeded atomically
        int queueSize = device.OutputQueue.Size;
        int shortage = Math.Max(framesRequested - queueSize, 0);
        System.Threading.Interlocked.Exchange(ref framesNeeded, shortage);

        // Size to 2x blocksize to avoid over-runs and stalls
        int queueCapacity = (int)Math.Ceiling(device.Channel.GetFramesPerBlock() * 2.0f);
        device.OutputQueue.Resize(queueCapacity);

        // Dequeue frames in bulk
        AudioFrame[] toMix = new AudioFrame[framesRequested];
        int framesReceived = device.OutputQueue.BulkDequeue(toMix, framesRequested);

        if (framesReceived > 0) {
            device.Channel.AddAudioFrames(toMix.AsSpan(0, framesReceived));
        }

        // Fill any shortfall with silence
        if (framesReceived < framesRequested) {
            device.Channel.AddSilence();
        }
    }
}
