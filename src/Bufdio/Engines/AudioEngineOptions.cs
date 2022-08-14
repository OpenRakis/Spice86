namespace Bufdio.Engines;

/// <summary>
/// Represents configuration class that can be passed to audio engine.
/// This class cannot be inherited.
/// </summary>
public sealed class AudioEngineOptions
{
    /// <summary>
    /// Initializes <see cref="AudioEngineOptions"/>.
    /// </summary>
    /// <param name="device">Desired output device, see: <see cref="BufdioLib.OutputDevices"/>.</param>
    /// <param name="channels">Desired audio channels, or fallback to maximum channels.</param>
    /// <param name="sampleRate">Desired output sample rate.</param>
    /// <param name="latency">Desired output latency.</param>
    public AudioEngineOptions(AudioDevice device, int channels, int sampleRate, double latency)
    {
        Device = device;
        Channels = FallbackChannelCount(Device, channels);
        SampleRate = sampleRate;
        Latency = latency;
    }

    /// <summary>
    /// Initializes <see cref="AudioEngineOptions"/> by using default output device.
    /// </summary>
    /// <param name="channels">Desired audio channels, or fallback to maximum channels.</param>
    /// <param name="sampleRate">Desired output sample rate.</param>
    /// <param name="latency">Desired output latency.</param>
    public AudioEngineOptions(int channels, int sampleRate, double latency)
    {
        Device = BufdioLib.DefaultOutputDevice;
        Channels = FallbackChannelCount(Device, channels);
        SampleRate = sampleRate;
        Latency = latency;
    }

    /// <summary>
    /// Initializes <see cref="AudioEngineOptions"/> by using default output device
    /// and its default high output latency.
    /// </summary>
    /// <param name="channels">Desired audio channels, or fallback to maximum channels.</param>
    /// <param name="sampleRate">Desired output sample rate.</param>
    public AudioEngineOptions(int channels, int sampleRate)
    {
        Device = BufdioLib.DefaultOutputDevice;
        Channels = FallbackChannelCount(Device, channels);
        SampleRate = sampleRate;
        Latency = Device.DefaultHighOutputLatency;
    }

    /// <summary>
    /// Initializes <see cref="AudioEngineOptions"/> by using default output device.
    /// Sample rate will be set to 44100, channels to 2 (or max) and latency to default high. 
    /// </summary>
    public AudioEngineOptions()
    {
        Device = BufdioLib.DefaultOutputDevice;
        Channels = FallbackChannelCount(Device, 2);
        SampleRate = 44100;
        Latency = Device.DefaultHighOutputLatency;
    }

    /// <summary>
    /// Gets desired output device.
    /// See: <see cref="BufdioLib.OutputDevices"/> and <see cref="BufdioLib.DefaultOutputDevice"/>.
    /// </summary>
    public AudioDevice Device { get; }

    /// <summary>
    /// Gets desired number of audio channels. This might fallback to maximum device output channels,
    /// see: <see cref="AudioDevice.MaxOutputChannels"/>.
    /// </summary>
    public int Channels { get; }

    /// <summary>
    /// Gets desired audio sample rate.
    /// </summary>
    public int SampleRate { get; }

    /// <summary>
    /// Gets desired output latency.
    /// </summary>
    public double Latency { get; }

    private static int FallbackChannelCount(AudioDevice device, int desiredChannel)
    {
        return desiredChannel > device.MaxOutputChannels ? device.MaxOutputChannels : desiredChannel;
    }
}
