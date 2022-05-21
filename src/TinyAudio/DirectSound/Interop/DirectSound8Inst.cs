namespace TinyAudio.DirectSound.Interop;

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[SupportedOSPlatform("windows")]
[StructLayout(LayoutKind.Sequential)]
internal struct DirectSound8Inst
{
    public unsafe DirectSound8V* Vtbl;
    }
    

