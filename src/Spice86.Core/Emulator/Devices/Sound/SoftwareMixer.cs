namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Backend.Audio;
using Spice86.Shared.Interfaces;

/// <summary>
/// DOSBox-compatible crossfeed presets.
/// </summary>
public enum CrossfeedPreset { None, Light, Normal, Strong }

/// <summary>
/// DOSBox-compatible reverb presets.
/// </summary>
public enum ReverbPreset { None, Tiny, Small, Medium, Large, Huge }

/// <summary>
/// DOSBox-compatible chorus presets.
/// </summary>
public enum ChorusPreset { None, Light, Normal, Strong }

/// <summary>
/// Basic software mixer with DOSBox mixer compatibility.
/// No locks needed due to DeviceThread architecture.
/// </summary>
public sealed class SoftwareMixer : IDisposable {
    private readonly Dictionary<SoundChannel, AudioPlayer> _channels = new();
    private readonly Dictionary<string, SoundChannel> _mixerChannels = new();
    private readonly AudioPlayerFactory _audioPlayerFactory;
    private bool _disposed;
    
    // DOSBox mixer settings
    private CrossfeedPreset _crossfeedPreset = CrossfeedPreset.None;
    private ReverbPreset _reverbPreset = ReverbPreset.None;
    private ChorusPreset _chorusPreset = ChorusPreset.None;
    
    private bool _doCrossfeed;
    private bool _doReverb;
    private bool _doChorus;

    /// <summary>
    /// Initializes a new instance of the <see cref="SoftwareMixer"/> class.
    /// </summary>
    public SoftwareMixer(ILoggerService loggerService, AudioEngine audioEngine) {
        _audioPlayerFactory = new(loggerService, audioEngine);
    }

    internal SoundChannel CreateChannel(string name) {
        SoundChannel soundChannel = new(this, name);
        return soundChannel;
    }

    internal void Register(SoundChannel soundChannel) {
        _channels.Add(soundChannel, _audioPlayerFactory.CreatePlayer(sampleRate: 48000, framesPerBuffer: 2048));
        Channels = _channels.AsReadOnly();
    }
    
    /// <summary>
    /// Gets the sound channels in a read-only dictionary.
    /// </summary>
    public IReadOnlyDictionary<SoundChannel, AudioPlayer> Channels { get; private set; } = new Dictionary<SoundChannel, AudioPlayer>().AsReadOnly();

    // DOSBox-compatible mixer channel management
    /// <summary>
    /// Adds a DOSBox-compatible mixer channel.
    /// </summary>
    public SoundChannel AddChannel(Action<int> handler, int sampleRateHz, string name, HashSet<ChannelFeature> features) {
        var channel = new SoundChannel(this, name, features, handler);
        channel.SetSampleRate(sampleRateHz == 0 ? 48000 : sampleRateHz);
        channel.SetAppVolume(1.0f, 1.0f);
        channel.Set0dbScalar(1.5f); // DOSBox OPL gain
        
        // Apply global effects settings
        SetGlobalCrossfeed(channel);
        SetGlobalReverb(channel);
        SetGlobalChorus(channel);
        
        _mixerChannels[name] = channel;
        return channel;
    }
    
    /// <summary>
    /// Finds a mixer channel by name.
    /// </summary>
    public SoundChannel? FindChannel(string name) {
        return _mixerChannels.TryGetValue(name, out SoundChannel? channel) ? channel : null;
    }
    
    /// <summary>
    /// Deregisters a mixer channel.
    /// </summary>
    public void DeregisterChannel(string name) {
        if (_mixerChannels.TryGetValue(name, out SoundChannel? channel)) {
            _mixerChannels.Remove(name);
            if (_channels.ContainsKey(channel)) {
                _channels[channel].Dispose();
                _channels.Remove(channel);
            }
        }
    }
    
    // DOSBox effects settings
    public CrossfeedPreset GetCrossfeedPreset() => _crossfeedPreset;
    
    public void SetCrossfeedPreset(CrossfeedPreset preset) {
        if (_crossfeedPreset == preset) return;
        
        _crossfeedPreset = preset;
        _doCrossfeed = preset != CrossfeedPreset.None;
        
        foreach (SoundChannel channel in _mixerChannels.Values) {
            SetGlobalCrossfeed(channel);
        }
    }
    
    public ReverbPreset GetReverbPreset() => _reverbPreset;
    
    public void SetReverbPreset(ReverbPreset preset) {
        _reverbPreset = preset;
        _doReverb = preset != ReverbPreset.None;
        
        foreach (SoundChannel channel in _mixerChannels.Values) {
            SetGlobalReverb(channel);
        }
    }
    
    public ChorusPreset GetChorusPreset() => _chorusPreset;
    
    public void SetChorusPreset(ChorusPreset preset) {
        _chorusPreset = preset;
        _doChorus = preset != ChorusPreset.None;
        
        foreach (SoundChannel channel in _mixerChannels.Values) {
            SetGlobalChorus(channel);
        }
    }
    
    private void SetGlobalCrossfeed(SoundChannel channel) {
        // Apply crossfeed only to OPL channels (matches DOSBox behavior)
        bool applyCrossfeed = channel.Name == "OPL" && channel.HasFeature(ChannelFeature.Stereo);
        
        if (!_doCrossfeed || !applyCrossfeed) {
            channel.SetCrossfeedStrength(0.0f);
        } else {
            float strength = _crossfeedPreset switch {
                CrossfeedPreset.Light => 0.20f,
                CrossfeedPreset.Normal => 0.40f,
                CrossfeedPreset.Strong => 0.60f,
                _ => 0.0f
            };
            channel.SetCrossfeedStrength(strength);
        }
    }
    
    private void SetGlobalReverb(SoundChannel channel) {
        if (!_doReverb || !channel.HasFeature(ChannelFeature.ReverbSend)) {
            channel.SetReverbLevel(0.0f);
        } else if (channel.HasFeature(ChannelFeature.Synthesizer)) {
            // DOSBox OPL reverb levels
            float level = _reverbPreset switch {
                ReverbPreset.Tiny => 0.65f,
                ReverbPreset.Small => 0.40f,
                ReverbPreset.Medium => 0.54f,
                ReverbPreset.Large => 0.70f,
                ReverbPreset.Huge => 0.85f,
                _ => 0.0f
            };
            channel.SetReverbLevel(level);
        }
    }
    
    private void SetGlobalChorus(SoundChannel channel) {
        if (!_doChorus || !channel.HasFeature(ChannelFeature.ChorusSend)) {
            channel.SetChorusLevel(0.0f);
        } else if (channel.HasFeature(ChannelFeature.Synthesizer)) {
            // DOSBox OPL chorus levels
            float level = _chorusPreset switch {
                ChorusPreset.Light => 0.33f,
                ChorusPreset.Normal => 0.54f,
                ChorusPreset.Strong => 0.75f,
                _ => 0.0f
            };
            channel.SetChorusLevel(level);
        }
    }
    
    // Existing render methods remain unchanged
    internal int Render(Span<float> data, SoundChannel channel) {
        if (channel.Volume == 0 || channel.IsMuted) {
            _channels[channel].WriteSilence();
            return data.Length;
        }
        
        float volumeFactor = channel.Volume / 100f;
        
        Span<float> target = stackalloc float[data.Length];
        for (int i = 0; i < data.Length; i++) {
            target[i] = data[i] * volumeFactor;
        }
        return _channels[channel].WriteData(target);
    }
    
    internal int Render(Span<short> data, SoundChannel channel) {
        if (channel.Volume == 0 || channel.IsMuted) {
            _channels[channel].WriteSilence();
            return data.Length;
        }
        
        float volumeFactor = channel.Volume / 100f;
        
        Span<float> target = stackalloc float[data.Length];
        for (int i = 0; i < data.Length; i++) {
            target[i] = (data[i] / 32768f) * volumeFactor;
        }

        return _channels[channel].WriteData(target);
    }
    
    internal int Render(Span<byte> data, SoundChannel channel) {
        if (channel.Volume == 0 || channel.IsMuted) {
            _channels[channel].WriteSilence();
            return data.Length;
        }
        
        float volumeFactor = channel.Volume / 100f;
        
        Span<float> target = stackalloc float[data.Length];
        for (int i = 0; i < data.Length; i++) {
            target[i] = ((data[i] - 127) / 128f) * volumeFactor;
        }
        return _channels[channel].WriteData(target);
    }
    
    private void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                foreach (AudioPlayer audioPlayer in _channels.Values) {
                    audioPlayer.Dispose();
                }
                _channels.Clear();
                _mixerChannels.Clear();
            }
            _disposed = true;
        }
    }

    /// <inheritdoc />
    public void Dispose() {
        Dispose(true);
    }
}