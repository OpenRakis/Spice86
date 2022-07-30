namespace Spice86.Core.Backend.Audio.OpenAl.Wasapi.Interop;

using System.Runtime.Versioning;

[SupportedOSPlatform("windows")]
internal enum EDataFlow : uint {
    eRender,
    eCapture,
    eAll
}
