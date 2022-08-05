namespace Spice86.Core.Backend.Audio.Wasapi.Interop;

using System.Runtime.Versioning;

[SupportedOSPlatform("windows")]
internal enum EDataFlow : uint {
    eRender,
    eCapture,
    eAll
}
