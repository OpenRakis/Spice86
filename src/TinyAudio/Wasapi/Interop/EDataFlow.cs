namespace TinyAudio.Wasapi.Interop;
using System.Runtime.Versioning;

[SupportedOSPlatform("windows")]
internal enum EDataFlow : uint
{
    eRender,
    eCapture,
    eAll
}
