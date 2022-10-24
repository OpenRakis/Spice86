namespace Bufdio.Spice86;

/// <summary>
/// A structure containing information about audio device capabilities.
/// </summary>
public readonly record struct AudioDevice
{
    /// <summary>
    /// Initializes <see cref="AudioDevice"/> structure.
    /// </summary>
    /// <param name="deviceIndex">Audio device index.</param>
    /// <param name="name">Audio device name.</param>
    /// <param name="maxOutputChannels">Maximum allowed output channels.</param>
    /// <param name="defaultLowOutputLatency">Default low output latency.</param>
    /// <param name="defaultHighOutputLatency">Default high output latency.</param>
    /// <param name="defaultSampleRate">Default audio sample rate in the device.</param>
    public AudioDevice(
        int deviceIndex,
        string name,
        int maxOutputChannels,
        double defaultLowOutputLatency,
        double defaultHighOutputLatency,
        int defaultSampleRate)
    {
        DeviceIndex = deviceIndex;
        Name = name;
        MaxOutputChannels = maxOutputChannels;
        DefaultLowOutputLatency = defaultLowOutputLatency;
        DefaultHighOutputLatency = defaultHighOutputLatency;
        DefaultSampleRate = defaultSampleRate;
    }

    /// <summary>
    /// Gets audio device index.
    /// </summary>
    public int DeviceIndex { get; init; }

    /// <summary>
    /// Gets audio device name.
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// Gets maximum allowed output audio channels.
    /// </summary>
    public int MaxOutputChannels { get; init; }

    /// <summary>
    /// Gets default low output latency (for interactive performance).
    /// </summary>
    public double DefaultLowOutputLatency { get; init; }

    /// <summary>
    /// Gets default high output latency (recommended for playing audio files).
    /// </summary>
    public double DefaultHighOutputLatency { get; init; }

    /// <summary>
    /// Gets default audio sample rate on this device.
    /// </summary>
    public int DefaultSampleRate { get; init; }
}
