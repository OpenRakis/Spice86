namespace Spice86.Backend.Audio.OpenAl.DirectSound.Interop;

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[SupportedOSPlatform(("windows"))]
[StructLayout(LayoutKind.Sequential)]
internal struct DSBUFFERDESC
{
    public uint dwSize;
    public uint dwFlags;
    public uint dwBufferBytes;
    public uint dwReserved;
    public unsafe WAVEFORMATEX* lpwfxFormat;
    public unsafe fixed byte guid3DAlgorithm[16];
}
