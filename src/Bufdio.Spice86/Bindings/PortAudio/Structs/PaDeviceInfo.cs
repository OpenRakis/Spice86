namespace Bufdio.Spice86.Bindings.PortAudio.Structs;

using System.Runtime.InteropServices;

/// <summary>
/// Information about a PortAudio device.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct PaDeviceInfo {
    /// <summary>
    /// Gets the structure version number.
    /// </summary>
    public readonly int structVersion;

    /// <summary>
    /// Gets the device name.
    /// </summary>
    [MarshalAs(UnmanagedType.LPStr)]
    public readonly string name;

    /// <summary>
    /// Gets the host API index.
    /// </summary>
    public readonly int hostApi;
    
    /// <summary>
    /// Gets the maximum number of input channels supported.
    /// </summary>
    public readonly int maxInputChannels;
    
    /// <summary>
    /// Gets the maximum number of output channels supported.
    /// </summary>
    public readonly int maxOutputChannels;
    
    /// <summary>
    /// Gets the default low input latency in seconds.
    /// </summary>
    public readonly double defaultLowInputLatency;
    
    /// <summary>
    /// Gets the default low output latency in seconds.
    /// </summary>
    public readonly double defaultLowOutputLatency;
    
    /// <summary>
    /// Gets the default high input latency in seconds.
    /// </summary>
    public readonly double defaultHighInputLatency;
    
    /// <summary>
    /// Gets the default high output latency in seconds.
    /// </summary>
    public readonly double defaultHighOutputLatency;
    
    /// <summary>
    /// Gets the default sample rate in Hz.
    /// </summary>
    public readonly double defaultSampleRate;
}