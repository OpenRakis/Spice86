namespace Bufdio.Spice86.Bindings.PortAudio.Structs;

using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct PaDeviceInfo {
    public readonly int structVersion;

    [MarshalAs(UnmanagedType.LPStr)]
    public readonly string name;

    public readonly int hostApi;
    public readonly int maxInputChannels;
    public readonly int maxOutputChannels;
    public readonly double defaultLowInputLatency;
    public readonly double defaultLowOutputLatency;
    public readonly double defaultHighInputLatency;
    public readonly double defaultHighOutputLatency;
    public readonly double defaultSampleRate;
}