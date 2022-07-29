namespace Spice86.Backend.Audio.OpenAl.Wasapi.Interop;

using System.Runtime.Versioning;

[SupportedOSPlatform(("windows"))]
internal enum ERole : uint
{
    eConsole,
    eMultimedia,
    eCommunications
}
