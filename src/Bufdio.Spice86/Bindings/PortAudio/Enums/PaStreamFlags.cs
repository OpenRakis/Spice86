namespace Bufdio.Spice86.Bindings.PortAudio.Enums;

internal enum PaStreamFlags : long {
    paNoFlag = 0,
    paClipOff = 0x00000001,
    paDitherOff = 0x00000002,
    paPrimeOutputBuffersUsingStreamCallback = 0x00000008,
    paPlatformSpecificFlags = 0xFFFF0000
}