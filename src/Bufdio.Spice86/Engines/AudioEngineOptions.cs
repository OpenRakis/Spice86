namespace Bufdio.Spice86.Engines;

/// <summary>
/// Represents configuration class that can be passed to audio engine.
/// This class cannot be inherited.
/// </summary>
public readonly record struct AudioEngineOptions
{
    /// <summary>
    /// Initializes <see cref="AudioEngineOptions"/>.
    /// </summary>
    /// <param name="defaultAudioDevice">Desired output device, see: <see cref="PortAudioLib.OutputDevices"/>.</param>
    /// <param name="channels">Desired audio channels, or fallback to maximum channels.</param>
    /// <param name="sampleRate">Desired output sample rate.</param>
    /// <param name="latency">Desired output latency.</param>
    public AudioEngineOptions(AudioDevice defaultOutputDevice, int channels, int sampleRate, double latency)
    {
        DefaultAudioDevice = defaultOutputDevice;
        Channels = FallbackChannelCount(DefaultAudioDevice, channels);
        SampleRate = sampleRate;
        Latency = latency;
    }

    /// <summary>
    /// Initializes <see cref="AudioEngineOptions"/> by using default output device
    /// and its default high output latency.
    /// </summary>
    /// <param name="defaultAudioDevice">Desired output device, see: <see cref="PortAudioLib.OutputDevices"/>.</param>
    /// <param name="channels">Desired audio channels, or fallback to maximum channels.</param>
    /// <param name="sampleRate">Desired output sample rate.</param>
    public AudioEngineOptions(AudioDevice defaultOutputDevice, int channels, int sampleRate)
    {
        DefaultAudioDevice = defaultOutputDevice;
        Channels = FallbackChannelCount(DefaultAudioDevice, channels);
        SampleRate = sampleRate;
        Latency = DefaultAudioDevice.DefaultLowOutputLatency;
    }

    /// <summary>
    /// Initializes <see cref="AudioEngineOptions"/> by using default output device.
    /// Sample rate will be set to 44100, channels to 2 (or max) and latency to default high.
    /// </summary>
    public AudioEngineOptions(AudioDevice defaultOutputDevice)
    {
        DefaultAudioDevice = defaultOutputDevice;
        Channels = FallbackChannelCount(DefaultAudioDevice, 2);
        SampleRate = 48000;
        Latency = DefaultAudioDevice.DefaultLowOutputLatency;
    }

    /// <summary>
    /// Gets desired output device.
    /// See: <see cref="PortAudioLib.OutputDevices"/> and <see cref="PortAudioLib.DefaultOutputDevice"/>.
    /// </summary>
    public AudioDevice DefaultAudioDevice { get; init; }

    /// <summary>
    /// Gets desired number of audio channels. This might fallback to maximum device output channels,
    /// see: <see cref="AudioDevice.MaxOutputChannels"/>.
    /// </summary>
    public int Channels { get; init; }

    /// <summary>
    /// Gets desired audio sample rate.
    /// </summary>
    public int SampleRate { get; init; }

    /// <summary>
    /// Gets desired output latency.
    /// </summary>
    public double Latency { get; init; }

    private static int FallbackChannelCount(AudioDevice device, int desiredChannel) => desiredChannel > device.MaxOutputChannels ? device.MaxOutputChannels : desiredChannel;
}
