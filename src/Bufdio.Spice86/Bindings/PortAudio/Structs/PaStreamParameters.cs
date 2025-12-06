namespace Bufdio.Spice86.Bindings.PortAudio.Structs;

using Bufdio.Spice86.Bindings.PortAudio.Enums;

using System;
using System.Runtime.InteropServices;

/// <summary>
/// Parameters for a PortAudio stream.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct PaStreamParameters {
    /// <summary>
    /// Gets the device index.
    /// </summary>
    public readonly int Device { get; init; }
    
    /// <summary>
    /// Gets the number of channels.
    /// </summary>
    public readonly int ChannelCount { get; init; }
    
    /// <summary>
    /// Gets the sample format.
    /// </summary>
    public readonly PaSampleFormat SampleFormat { get; init; }
    
    /// <summary>
    /// Gets the suggested latency in seconds.
    /// </summary>
    public readonly double SuggestedLatency { get; init; }
    
    /// <summary>
    /// Gets a pointer to host API-specific stream information.
    /// </summary>
    public readonly IntPtr HostApiSpecificStreamInfo { get; init; }
}