namespace Bufdio.Spice86.Bindings.PortAudio.Enums;

internal enum PaStreamCallbackFlags : long {
    paInputUnderflow = 0x00000001,
    paInputOverflow = 0x00000002,
    paOutputUnderflow = 0x00000004,
    paOutputOverflow = 0x00000008,
    paPrimingOutput = 0x00000010
}