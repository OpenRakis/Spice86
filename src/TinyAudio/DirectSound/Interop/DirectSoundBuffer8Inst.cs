namespace TinyAudio.DirectSound.Interop;

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[SupportedOSPlatform("windows")]
[StructLayout(LayoutKind.Sequential)]
internal struct DirectSoundBuffer8Inst
{
    public unsafe DirectSoundBuffer8V* Vtbl;
}
