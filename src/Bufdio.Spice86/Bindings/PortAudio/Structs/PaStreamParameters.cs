namespace Bufdio.Spice86.Bindings.PortAudio.Structs;

using System;
using System.Runtime.InteropServices;

using Bufdio.Spice86.Bindings.PortAudio.Enums;

[StructLayout(LayoutKind.Sequential)]
internal record struct PaStreamParameters
{
    public int device;
    public int channelCount;
    public PaSampleFormat sampleFormat;
    public double suggestedLatency;
    public IntPtr hostApiSpecificStreamInfo;
}