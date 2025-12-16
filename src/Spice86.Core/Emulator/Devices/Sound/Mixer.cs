namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Backend.Audio;
using Spice86.Libs.Sound.Common;
using Spice86.Shared.Interfaces;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
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
    private volatile int _sampleRateHz = DefaultSampleRateHz;
    private volatile int _blocksize = DefaultBlocksize;
   
    // Master volume (atomic via Interlocked operations)
    private AudioFrame _masterGain = new(Minus6db, Minus6db);
    
    // Effect presets - mirrors DOSBox preset system
    private CrossfeedPreset _crossfeedPreset = CrossfeedPreset.None;
    private ReverbPreset _reverbPreset = ReverbPreset.None;
    private ChorusPreset _chorusPreset = ChorusPreset.None;
    
    // Output buffers - matches DOSBox mixer output_buffer
    private readonly List<AudioFrame> _outputBuffer = new();
    private readonly List<AudioFrame> _reverbAuxBuffer = new();
    private readonly List<AudioFrame> _chorusAuxBuffer = new();
    
    // Compressor state - mirrors DOSBox compressor (mixer.cpp lines 194-195, 659-686)
    private bool _doCompressor = false;
    private readonly Compressor _compressor = new();
    
    // Normalization state - mirrors DOSBox peak detection
    private float _peakLeft = 0.0f;
    private float _peakRight = 0.0f;
    private const float PeakDecayCoeff = 0.995f; // Slow decay for peak tracking
    
    // Reverb state - MVerb professional algorithmic reverb
    // Mirrors DOSBox mixer.cpp ReverbSettings (lines 78-120)
    private bool _doReverb = false;
    private readonly MVerb _mverb = new();
    private float _reverbSynthSendLevel = 0.0f;
    private float _reverbDigitalSendLevel = 0.0f;
    
    // Chorus state - TAL-Chorus professional modulated chorus
    // Mirrors DOSBox mixer.cpp ChorusSettings (lines 127-151)
    private bool _doChorus = false;
    private readonly ChorusEngine _chorusEngine;
    private float _chorusSynthSendLevel = 0.0f;
    private float _chorusDigitalSendLevel = 0.0f;
    
    // Crossfeed state - stereo mixing for headphone spatialization
    private bool _doCrossfeed = false;
    private const float CrossfeedStrength = 0.3f; // 30% mix to opposite channel
    
    // High-pass filters - mirrors DOSBox HighpassFilter
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
        // Mirrors DOSBox HighpassFilter = std::array<Iir::Butterworth::HighPass<2>, 2>
        _reverbHighPassFilter = new HighPassFilter[2];
        _masterHighPassFilter = new HighPassFilter[2];
        
        for (int i = 0; i < 2; i++) {
            _reverbHighPassFilter[i] = new HighPassFilter(HighPassFilterOrder);
            _reverbHighPassFilter[i].Setup(HighPassFilterOrder, _sampleRateHz, ReverbHighPassCutoffHz);
            
            _masterHighPassFilter[i] = new HighPassFilter(HighPassFilterOrder);
            _masterHighPassFilter[i].Setup(HighPassFilterOrder, _sampleRateHz, MasterHighPassCutoffHz);
        }
        
        // Initialize MVerb with default parameters
        // Mirrors DOSBox ReverbSettings setup (mixer.cpp lines 94-120)
        _mverb.SetSampleRate(_sampleRateHz);
        
        // Initialize ChorusEngine with default sample rate
        // Mirrors DOSBox ChorusSettings setup (mixer.cpp lines 127-151)
        _chorusEngine = new ChorusEngine(_sampleRateHz);
        
        // Configure chorus: Chorus1 enabled, Chorus2 disabled (matches DOSBox)
        // See DOSBox mixer.cpp lines 146-147
        _chorusEngine.SetEnablesChorus(isChorus1Enabled: true, isChorus2Enabled: false);
        
        // Initialize compressor with default parameters
        // Mirrors DOSBox init_compressor() (mixer.cpp lines 659-686)
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
    /// Mirrors DOSBox MIXER_GetSampleRate() from mixer.cpp:250
    /// </summary>
    public int SampleRateHz => _sampleRateHz;

    /// <summary>
    /// Gets the current blocksize.
    /// </summary>
    public int Blocksize => _blocksize;
    
    /// <summary>
    /// Gets the prebuffer time in milliseconds.
    /// Mirrors DOSBox MIXER_GetPreBufferMs() from mixer.cpp:242
    /// </summary>
    public int GetPreBufferMs() {
        // For now return a constant; DOSBox calculates based on buffer size
        return MaxPrebufferMs / 2; // Conservative default
    }
    
    /// <summary>
    /// Locks the mixer thread to prevent mixing during critical operations.
    /// Mirrors DOSBox MIXER_LockMixerThread() from mixer.cpp:279
    /// Note: DOSBox also stops device queues; we just lock the mixer.
    /// Use within a using statement or with UnlockMixerThread().
    /// </summary>
    public void LockMixerThread() {
        _mixerLock.Enter();
    }
    
    /// <summary>
    /// Unlocks the mixer thread after critical operations complete.
    /// Mirrors DOSBox MIXER_UnlockMixerThread() from mixer.cpp:292
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
    /// Gets the current crossfeed preset.
    /// Mirrors DOSBox MIXER_GetCrossfeedPreset().
    /// </summary>
    public CrossfeedPreset GetCrossfeedPreset() {
        lock (_mixerLock) {
            return _crossfeedPreset;
        }
    }
    
    /// <summary>
    /// Sets the crossfeed preset and configures the effect.
    /// Mirrors DOSBox MIXER_SetCrossfeedPreset().
    /// </summary>
    public void SetCrossfeedPreset(CrossfeedPreset preset) {
        lock (_mixerLock) {
            if (_crossfeedPreset == preset) {
                return;
            }
            
            _crossfeedPreset = preset;
            _doCrossfeed = preset != CrossfeedPreset.None;
            
            if (_doCrossfeed) {
                _loggerService.Information("MIXER: Crossfeed enabled ('{Preset}' preset)", preset);
            } else {
                _loggerService.Information("MIXER: Crossfeed disabled");
            }
            
            // Update all registered channels - mirrors DOSBox set_global_crossfeed
            SetGlobalCrossfeed();
        }
    }
    
    /// <summary>
    /// Applies global crossfeed settings to all channels.
    /// Mirrors DOSBox set_global_crossfeed() from mixer.cpp:333
    /// </summary>
    private void SetGlobalCrossfeed() {
        // DOSBox applies crossfeed to OPL and CMS channels only
        // For now, we apply based on channel features
        float globalStrength = _doCrossfeed ? CrossfeedStrength : 0.0f;
        
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
    /// Mirrors DOSBox MIXER_GetReverbPreset().
    /// </summary>
    public ReverbPreset GetReverbPreset() {
        lock (_mixerLock) {
            return _reverbPreset;
        }
    }
    
    /// <summary>
    /// Sets the reverb preset and configures the effect.
    /// Mirrors DOSBox MIXER_SetReverbPreset() (mixer.cpp lines 519-558).
    /// </summary>
    public void SetReverbPreset(ReverbPreset preset) {
        lock (_mixerLock) {
            if (_reverbPreset == preset) {
                return;
            }
            
            _reverbPreset = preset;
            
            // Configure MVerb based on preset - mirrors DOSBox switch statement (lines 534-540)
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
            
            // Update all registered channels - mirrors DOSBox set_global_reverb
            SetGlobalReverb();
        }
    }
    
    /// <summary>
    /// Configures MVerb reverb parameters.
    /// Mirrors DOSBox ReverbSettings::Setup() (mixer.cpp lines 94-120).
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
    /// Mirrors DOSBox set_global_reverb() from mixer.cpp:348-362
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
    /// Mirrors DOSBox MIXER_GetChorusPreset().
    /// </summary>
    public ChorusPreset GetChorusPreset() {
        lock (_mixerLock) {
            return _chorusPreset;
        }
    }
    
    /// <summary>
    /// Sets the chorus preset and configures the effect.
    /// Mirrors DOSBox MIXER_SetChorusPreset() from mixer.cpp:619-656.
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
            
            // Update all registered channels - mirrors DOSBox set_global_chorus
            SetGlobalChorus();
        }
    }
    
    /// <summary>
    /// Applies global chorus settings to all channels.
    /// Mirrors DOSBox set_global_chorus() from mixer.cpp:363-376.
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
    /// Main mixer thread loop - mirrors mixer_thread_loop from DOSBox.
    /// </summary>
    private void MixerThreadLoop() {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
            _loggerService.Debug("MIXER: Mixer thread started. sampleRate={SampleRateHz}, blocksize={Blocksize}", _sampleRateHz, _blocksize);
        }
        
        CancellationToken token = _cancellationTokenSource.Token;

        try {
            while (!_threadShouldQuit && !token.IsCancellationRequested) {
                // Mix one blocksize worth of frames
                int framesRequested = _blocksize;
                
                if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                    _loggerService.Verbose("MIXER: Begin mix cycle frames={FramesRequested} channels={ChannelCount}", framesRequested, _channels.Count);
                }

                lock (_mixerLock) {
                    MixSamples(framesRequested);
                }

                if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                    // Summarize per-channel contributions similar to DOSBox's trace
                    foreach (KeyValuePair<string, MixerChannel> kvp in _channels) {
                        MixerChannel ch = kvp.Value;
                        if (!ch.IsEnabled) {
                            continue;
                        }
                        int contributed = Math.Min(framesRequested, ch.AudioFrames.Count);
                        string features = string.Join(",", ch.GetFeatures());
                        _loggerService.Verbose("MIXER: Channel={Channel} contributed={Frames} rate={Rate} features={Features}", ch.GetName(), contributed, ch.GetSampleRate(), features);
                    }
                }

                // Write the mixed block directly to PortAudio (mirror DOSBox behavior)
                try {
                    int framesToWrite = _outputBuffer.Count;
                    if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                        _loggerService.Verbose("MIXER: Mixed frames={Frames}", framesToWrite);
                    }
                    if (framesToWrite > 0) {
                        float[] temp = System.Buffers.ArrayPool<float>.Shared.Rent(framesToWrite * 2);
                        try {
                            Span<float> interleavedBuffer = temp.AsSpan(0, framesToWrite * 2);
                            const float normalizeFactor = 1.0f / 32768.0f;
                            for (int i = 0; i < framesToWrite; i++) {
                                int offset = i * 2;
                                interleavedBuffer[offset] = _outputBuffer[i].Left * normalizeFactor;
                                interleavedBuffer[offset + 1] = _outputBuffer[i].Right * normalizeFactor;
                            }
                            _audioPlayer.WriteData(interleavedBuffer);
                            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                                _loggerService.Verbose("MIXER: Wrote frames to PortAudio frames={Frames}", framesToWrite);
                            }
                        } finally {
                            System.Buffers.ArrayPool<float>.Shared.Return(temp);
                        }
                    }
                } catch (Exception ex) {
                    _loggerService.Error(ex, "MIXER: Failed writing audio block to PortAudio");
                }

            }
        } catch (Exception ex) {
            _loggerService.Error(ex, "MIXER: Mixer thread encountered an error");
        } finally {
            _loggerService.Debug("MIXER: Mixer thread stopped");
        }
    }

    // No consumer thread: mixer thread writes directly to the audio backend

    /// <summary>
    /// Mix samples from all channels - mirrors mix_samples from DOSBox.
    /// </summary>
    private void MixSamples(int framesRequested) {
        // Clear output buffers
        _outputBuffer.Clear();
        _outputBuffer.Capacity = Math.Max(_outputBuffer.Capacity, framesRequested);
        
        _reverbAuxBuffer.Clear();
        _chorusAuxBuffer.Clear();

        // Initialize with silence
        for (int i = 0; i < framesRequested; i++) {
            _outputBuffer.Add(new AudioFrame(0.0f, 0.0f));
            _reverbAuxBuffer.Add(new AudioFrame(0.0f, 0.0f));
            _chorusAuxBuffer.Add(new AudioFrame(0.0f, 0.0f));
        }

        // Mix all enabled channels
        foreach (MixerChannel channel in _channels.Values) {
            if (!channel.IsEnabled) {
                continue;
            }

            // Request frames from the channel
            channel.Mix(framesRequested);

            // Accumulate channel output into master mix
            // Mirrors DOSBox mixer.cpp:2418-2435
            int numFrames = Math.Min(framesRequested, channel.AudioFrames.Count);
            for (int i = 0; i < numFrames; i++) {
                AudioFrame channelFrame = channel.AudioFrames[i];
                
                // Apply sleep/wake fade-out or signal detection if enabled
                // Mirrors DOSBox mixer.cpp:2419-2425
                channelFrame = channel.MaybeFadeOrListen(channelFrame);
                
                // Add to master output using operator
                _outputBuffer[i] = _outputBuffer[i] + channelFrame;
                
                // Reverb and chorus sends - mirrors DOSBox logic
                if (_doReverb && channel.DoReverbSend) {
                    _reverbAuxBuffer[i] = _reverbAuxBuffer[i] + (channelFrame * channel.ReverbSendGain);
                }
                
                if (_doChorus && channel.DoChorusSend) {
                    _chorusAuxBuffer[i] = _chorusAuxBuffer[i] + (channelFrame * channel.ChorusSendGain);
                }
            }

            // Remove consumed frames from channel
            if (numFrames > 0) {
                channel.AudioFrames.RemoveRange(0, numFrames);
                if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                    _loggerService.Verbose("MIXER: Channel {Channel} provided frames={Frames}", channel.GetName(), numFrames);
                }
            }
            
            // Check if channel should sleep - mirrors DOSBox mixer.cpp:2440-2442
            channel.MaybeSleep();
        }

        // Apply master gain using operator
        AudioFrame masterGainSnapshot = _masterGain;
        for (int i = 0; i < _outputBuffer.Count; i++) {
            _outputBuffer[i] = _outputBuffer[i] * masterGainSnapshot;
        }

        // Apply effects pipeline - mirrors DOSBox effects order
        if (_doReverb) {
            // Apply high-pass filter to reverb aux buffer before reverb processing
            // Mirrors DOSBox mixer.cpp:2453-2462
            for (int i = 0; i < _reverbAuxBuffer.Count; i++) {
                AudioFrame frame = _reverbAuxBuffer[i];
                frame = new AudioFrame(
                    _reverbHighPassFilter[0].Filter(frame.Left),
                    _reverbHighPassFilter[1].Filter(frame.Right)
                );
                _reverbAuxBuffer[i] = frame;
            }
            
            ApplyReverb();
        }
        
        if (_doChorus) {
            ApplyChorus();
        }
        
        if (_doCrossfeed) {
            ApplyCrossfeed();
        }
        
        // Apply high-pass filter to master output - mirrors DOSBox mixer.cpp:2488-2491
        // This is a DC-blocking filter to prevent low-frequency buildup
        for (int i = 0; i < _outputBuffer.Count; i++) {
            AudioFrame frame = _outputBuffer[i];
            frame = new AudioFrame(
                _masterHighPassFilter[0].Filter(frame.Left),
                _masterHighPassFilter[1].Filter(frame.Right)
            );
            _outputBuffer[i] = frame;
        }
        
        // Apply compressor if enabled - mirrors DOSBox compressor
        if (_doCompressor) {
            ApplyCompressor();
        }
        
        // Apply master normalization - mirrors DOSBox peak detection
        ApplyMasterNormalization();
    }
    
    /// <summary>
    /// Initializes the master compressor with professional RMS-based configuration.
    /// Mirrors DOSBox init_compressor() from mixer.cpp:659-686
    /// </summary>
    /// <param name="compressorEnabled">Whether to enable the compressor</param>
    private void InitCompressor(bool compressorEnabled) {
        _doCompressor = compressorEnabled;
        if (!_doCompressor) {
            _loggerService.Information("MIXER: Master compressor disabled");
            return;
        }
        
        LockMixerThread();
        
        // Configuration values mirror DOSBox exactly (mixer.cpp:669-680)
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
    /// Mirrors DOSBox Compressor processing from mixer.cpp:2493-2498
    /// </summary>
    private void ApplyCompressor() {
        // Process each frame through the compressor
        // Mirrors DOSBox mixer.cpp:2494-2496
        for (int i = 0; i < _outputBuffer.Count; i++) {
            _outputBuffer[i] = _compressor.Process(_outputBuffer[i]);
        }
    }
    
    /// <summary>
    /// Applies master normalization to prevent clipping and maintain consistent levels.
    /// Mirrors DOSBox peak detection and soft limiting.
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
    /// Mirrors DOSBox reverb processing (mixer.cpp lines 2445-2467).
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
            leftIn[i] = (float)_reverbHighPassFilter[0].Filter(frame.Left);
            rightIn[i] = (float)_reverbHighPassFilter[1].Filter(frame.Right);
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
    /// Applies simple chorus effect using fixed delay.
    /// Simplified implementation mirroring DOSBox chorus concept.
    /// </summary>
    /// <summary>
    /// Applies TAL-Chorus effect to the chorus aux buffer and mixes to output.
    /// Mirrors DOSBox mixer.cpp:2470-2478.
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
        // Mirrors DOSBox mixer.cpp:2470-2478
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
    /// Mirrors DOSBox crossfeed mixing matrix.
    /// </summary>
    private void ApplyCrossfeed() {
        for (int i = 0; i < _outputBuffer.Count; i++) {
            AudioFrame frame = _outputBuffer[i];
            
            // Mix some of each channel into the opposite channel
            // This simulates speaker crosstalk for headphone listening
            float newLeft = frame.Left + frame.Right * CrossfeedStrength;
            float newRight = frame.Right + frame.Left * CrossfeedStrength;
            
            _outputBuffer[i] = new AudioFrame(newLeft, newRight);
        }
    }

    // ConsumeOutputQueue removed; direct write path used

    public void Dispose() {
        if (_disposed) {
            return;
        }

        _loggerService.Debug("MIXER: Disposing mixer");

        _threadShouldQuit = true;
        _cancellationTokenSource.Cancel();
        
        // Wait for mixer thread to stop producing frames
        if (_mixerThread.IsAlive) {
            _mixerThread.Join(TimeSpan.FromSeconds(5));
        }

        // No output queue or consumer thread to stop in direct-write mode

        _cancellationTokenSource.Dispose();
        _audioPlayer.Dispose();
        _chorusEngine.Dispose();

        _disposed = true;
    }

    // AudioFrameQueue removed: DOSBox writes directly from mixer thread
}
