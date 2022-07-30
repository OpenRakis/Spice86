namespace Spice86.Core.Backend.Audio.OpenAl.Wasapi.Interop;

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[SupportedOSPlatform("windows")]
internal static class NativeMethods {
    [DllImport("ole32.dll")]
    public static extern unsafe uint CoCreateInstance(Guid* rclsid, void** pUnkOuter, uint dwClsContext, Guid* riid, void** ppv);
}
