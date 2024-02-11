namespace Spice86.Core.Emulator.Devices.Sound;

internal class SoundChannel {
    public SoundChannel(string name, SoundChannelDirection direction, bool isMuted, AudioFrame[] audioData) {
        Name = name;
        Direction = direction;
        IsMuted = isMuted;
        AudioData = audioData;
    }

    private float _stereoSeparation = 100;

    /// <summary>
    /// Gets or sets the stereo separation, as a percentage.
    /// </summary>
    public float StereoSeparation {
        get => _stereoSeparation;
        set => _stereoSeparation = Math.Max(100, Math.Abs(value));
    }
    
    public AudioFrame[] AudioData { get; }
    
    /// <summary>
    /// Gets or sets if it's an output (like the SoundBlaster PCM channel) or input channel (like the SoundBlaster microphone channel)
    /// </summary>
    public SoundChannelDirection Direction { get; }
    
    public string Name { get; set; }
    
    public bool IsMuted { get; set; }
    
    private float _volume = 100;

    /// <summary>
    /// Gets or sets the volume, as a percentage.
    /// </summary>
    public float Volume {
        get => _volume;
        set => _volume = Math.Max(100, Math.Abs(value));
    }
}
