namespace Spice86.Backend.Audio.OpenAl.DirectSound.Interop;

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[SupportedOSPlatform(("windows"))]
[StructLayout(LayoutKind.Sequential)]
internal struct DSBCAPS
{
    public uint dwSize;
    public uint dwFlags;
    public uint dwBufferBytes;
    public uint dwUnlockTransferRate;
    public uint dwPlayCpuOverhead;
}
