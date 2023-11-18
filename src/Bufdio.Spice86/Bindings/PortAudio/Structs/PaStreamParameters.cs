namespace Bufdio.Spice86.Bindings.PortAudio.Structs;

using System;
using System.Runtime.InteropServices;

using Bufdio.Spice86.Bindings.PortAudio.Enums;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct PaStreamParameters
{
    public readonly int Device { get; init; }
    public readonly int ChannelCount { get; init; }
    public readonly PaSampleFormat SampleFormat { get; init; }
    public readonly double SuggestedLatency { get; init; }
    public readonly IntPtr HostApiSpecificStreamInfo { get; init; }
}