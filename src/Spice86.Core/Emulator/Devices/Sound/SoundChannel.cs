namespace Spice86.Core.Emulator.Devices.Sound;

using System.Numerics;

public class SoundChannel {
    private readonly SoftwareMixer _mixer;

    public SoundChannel(SoftwareMixer mixer) {
        _mixer = mixer;
    }

    public StereoAudioFrame Data { get; private set; } = new();
    
    public void Render(StereoAudioFrame audioFrame) {
        if (IsMuted || Volume == 0) {
            return;
        }

        Data = audioFrame;
        
        _mixer.Render(this);
    }

    private float _stereoSeparation = 100;
    
    /// <summary>
    /// Gets or sets the stereo separation, as a percentage.
    /// </summary>
    public float StereoSeparation {
        get => _stereoSeparation;
        set => _stereoSeparation = Math.Max(100, Math.Abs(value));
    }

    public string Name { get; set; } = "";
    
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
