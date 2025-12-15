namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Backend.Audio;
using Spice86.Libs.Sound.Common;
using Spice86.Shared.Interfaces;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

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
    
    // Compressor state - mirrors DOSBox compressor
    private bool _doCompressor = false;
    private float _compressorThreshold = 0.5f; // -6dB threshold
    private float _compressorRatio = 4.0f; // 4:1 compression ratio
    private float _compressorPeakLevel = 0.0f;
    private const float CompressorAttackCoeff = 0.999f;
    private const float CompressorReleaseCoeff = 0.9999f;
    
    // Normalization state - mirrors DOSBox peak detection
    private float _peakLeft = 0.0f;
    private float _peakRight = 0.0f;
    private const float PeakDecayCoeff = 0.995f; // Slow decay for peak tracking
    
    // Reverb state - simplified implementation using delay + feedback
    private bool _doReverb = false;
    private readonly List<AudioFrame> _reverbDelayBuffer = new();
    private const int ReverbDelayFrames = 2400; // ~50ms at 48kHz
    private const float ReverbFeedback = 0.3f;
    private const float ReverbMix = 0.2f;
    private int _reverbDelayIndex = 0;
    
    // Chorus state - delay buffer with LFO
    private bool _doChorus = false;
    private readonly List<AudioFrame> _chorusDelayBuffer = new();
    private const int ChorusDelayFrames = 960; // ~20ms at 48kHz
    private const float ChorusMix = 0.15f;
    private int _chorusDelayIndex = 0;
    
    // Crossfeed state - stereo mixing for headphone spatialization
    private bool _doCrossfeed = false;
    private const float CrossfeedStrength = 0.3f; // 30% mix to opposite channel
    
    // High-pass filters - mirrors DOSBox HighpassFilter
    // Used on reverb input and master output
    private readonly Spice86.Libs.Sound.Filters.IirFilters.Filters.Butterworth.HighPass[] _reverbHighPassFilter;
    private readonly Spice86.Libs.Sound.Filters.IirFilters.Filters.Butterworth.HighPass[] _masterHighPassFilter;
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
        _reverbHighPassFilter = new Spice86.Libs.Sound.Filters.IirFilters.Filters.Butterworth.HighPass[2];
        _masterHighPassFilter = new Spice86.Libs.Sound.Filters.IirFilters.Filters.Butterworth.HighPass[2];
        
        for (int i = 0; i < 2; i++) {
            _reverbHighPassFilter[i] = new Spice86.Libs.Sound.Filters.IirFilters.Filters.Butterworth.HighPass(HighPassFilterOrder);
            _reverbHighPassFilter[i].Setup(HighPassFilterOrder, _sampleRateHz, ReverbHighPassCutoffHz);
            
            _masterHighPassFilter[i] = new Spice86.Libs.Sound.Filters.IirFilters.Filters.Butterworth.HighPass(HighPassFilterOrder);
            _masterHighPassFilter[i].Setup(HighPassFilterOrder, _sampleRateHz, MasterHighPassCutoffHz);
        }
        
        // Initialize effect delay buffers with silence
        for (int i = 0; i < ReverbDelayFrames; i++) {
            _reverbDelayBuffer.Add(new AudioFrame(0.0f, 0.0f));
        }
        for (int i = 0; i < ChorusDelayFrames; i++) {
            _chorusDelayBuffer.Add(new AudioFrame(0.0f, 0.0f));
        }
        
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
    /// Mirrors DOSBox MIXER_SetReverbPreset().
    /// </summary>
    public void SetReverbPreset(ReverbPreset preset) {
        lock (_mixerLock) {
            if (_reverbPreset == preset) {
                return;
            }
            
            _reverbPreset = preset;
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
    /// Applies global reverb settings to all channels.
    /// Mirrors DOSBox set_global_reverb() from mixer.cpp:348
    /// </summary>
    private void SetGlobalReverb() {
        const float synthLevel = 0.3f;      // Default synthesizer send level
        const float digitalLevel = 0.2f;    // Default digital audio send level
        
        foreach (MixerChannel channel in _channels.Values) {
            if (!_doReverb || !channel.HasFeature(ChannelFeature.ReverbSend)) {
                channel.SetReverbLevel(0.0f);
            } else if (channel.HasFeature(ChannelFeature.Synthesizer)) {
                channel.SetReverbLevel(synthLevel);
            } else if (channel.HasFeature(ChannelFeature.DigitalAudio)) {
                channel.SetReverbLevel(digitalLevel);
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
    /// Mirrors DOSBox MIXER_SetChorusPreset().
    /// </summary>
    public void SetChorusPreset(ChorusPreset preset) {
        lock (_mixerLock) {
            if (_chorusPreset == preset) {
                return;
            }
            
            _chorusPreset = preset;
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
    /// Mirrors DOSBox set_global_chorus() from mixer.cpp:363
    /// </summary>
    private void SetGlobalChorus() {
        const float synthLevel = 0.25f;     // Default synthesizer send level
        const float digitalLevel = 0.15f;   // Default digital audio send level
        
        foreach (MixerChannel channel in _channels.Values) {
            if (!_doChorus || !channel.HasFeature(ChannelFeature.ChorusSend)) {
                channel.SetChorusLevel(0.0f);
            } else if (channel.HasFeature(ChannelFeature.Synthesizer)) {
                channel.SetChorusLevel(synthLevel);
            } else if (channel.HasFeature(ChannelFeature.DigitalAudio)) {
                channel.SetChorusLevel(digitalLevel);
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
    /// Applies simple compressor to reduce dynamic range.
    /// Mirrors DOSBox Compressor logic with peak detection and gain reduction.
    /// </summary>
    private void ApplyCompressor() {
        for (int i = 0; i < _outputBuffer.Count; i++) {
            AudioFrame frame = _outputBuffer[i];
            
            // Detect peak level (max of L/R channels)
            float peakSample = Math.Max(Math.Abs(frame.Left), Math.Abs(frame.Right));
            
            // Update peak tracker with attack/release
            if (peakSample > _compressorPeakLevel) {
                // Attack - fast response to louder signals
                _compressorPeakLevel = _compressorPeakLevel * CompressorAttackCoeff + 
                                       peakSample * (1.0f - CompressorAttackCoeff);
            } else {
                // Release - slow return to quiet
                _compressorPeakLevel = _compressorPeakLevel * CompressorReleaseCoeff + 
                                       peakSample * (1.0f - CompressorReleaseCoeff);
            }
            
            // Calculate gain reduction if above threshold
            float gainReduction = 1.0f;
            if (_compressorPeakLevel > _compressorThreshold) {
                // Apply compression ratio
                float overshoot = _compressorPeakLevel - _compressorThreshold;
                float compressed = overshoot / _compressorRatio;
                gainReduction = (_compressorThreshold + compressed) / _compressorPeakLevel;
            }
            
            // Apply gain reduction
            _outputBuffer[i] = new AudioFrame(
                frame.Left * gainReduction,
                frame.Right * gainReduction
            );
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
    /// Applies simple reverb effect using delay buffer with feedback.
    /// Simplified implementation mirroring DOSBox reverb concept.
    /// </summary>
    private void ApplyReverb() {
        for (int i = 0; i < _outputBuffer.Count; i++) {
            AudioFrame dry = _outputBuffer[i];
            
            // Get delayed signal from circular buffer
            AudioFrame delayed = _reverbDelayBuffer[_reverbDelayIndex];
            
            // Mix delayed signal back with feedback
            AudioFrame feedback = new AudioFrame(
                delayed.Left * ReverbFeedback,
                delayed.Right * ReverbFeedback
            );
            
            // Store new input + feedback in delay buffer
            _reverbDelayBuffer[_reverbDelayIndex] = new AudioFrame(
                dry.Left + feedback.Left,
                dry.Right + feedback.Right
            );
            
            // Mix wet (reverb) with dry signal
            _outputBuffer[i] = new AudioFrame(
                dry.Left + delayed.Left * ReverbMix,
                dry.Right + delayed.Right * ReverbMix
            );
            
            // Advance delay index (circular buffer)
            _reverbDelayIndex = (_reverbDelayIndex + 1) % ReverbDelayFrames;
        }
    }
    
    /// <summary>
    /// Applies simple chorus effect using fixed delay.
    /// Simplified implementation mirroring DOSBox chorus concept.
    /// </summary>
    private void ApplyChorus() {
        for (int i = 0; i < _outputBuffer.Count; i++) {
            AudioFrame dry = _outputBuffer[i];
            
            // Get delayed signal
            AudioFrame delayed = _chorusDelayBuffer[_chorusDelayIndex];
            
            // Store current in delay buffer
            _chorusDelayBuffer[_chorusDelayIndex] = dry;
            
            // Mix delayed signal with dry
            _outputBuffer[i] = new AudioFrame(
                dry.Left + delayed.Left * ChorusMix,
                dry.Right + delayed.Right * ChorusMix
            );
            
            // Advance delay index
            _chorusDelayIndex = (_chorusDelayIndex + 1) % ChorusDelayFrames;
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

        _disposed = true;
    }

    // AudioFrameQueue removed: DOSBox writes directly from mixer thread
}
