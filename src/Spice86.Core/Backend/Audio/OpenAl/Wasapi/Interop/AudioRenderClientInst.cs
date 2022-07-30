namespace Spice86.Core.Backend.Audio.OpenAl.Wasapi.Interop;

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[SupportedOSPlatform("windows")]
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct AudioRenderClientInst {
    public AudioRenderClientV* Vtbl;
}
