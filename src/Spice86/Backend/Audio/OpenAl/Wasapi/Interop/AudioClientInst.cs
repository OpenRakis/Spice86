namespace Spice86.Backend.Audio.OpenAl.Wasapi.Interop;

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[SupportedOSPlatform(("windows"))]
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct AudioClientInst
{
    public AudioClientV* Vtbl;
}
