namespace Spice86.Backend.Audio.OpenAl.Wasapi.Interop;

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[SupportedOSPlatform(("windows"))]
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct DeviceEnumeratorV
{
    public delegate* unmanaged[Stdcall]<DeviceEnumeratorInst*, Guid*, void**, uint> QueryInterface;
    public delegate* unmanaged[Stdcall]<DeviceEnumeratorInst*, uint> AddRef;
    public delegate* unmanaged[Stdcall]<DeviceEnumeratorInst*, uint> Release;

    public IntPtr EnumAudioEndpoints;
    public delegate* unmanaged[Stdcall]<DeviceEnumeratorInst*, EDataFlow, ERole, MMDeviceInst**, uint> GetDefaultAudioEndpoint;
    public IntPtr GetDevice;
    public IntPtr RegisterEndpointNotificationCallback;
    public IntPtr UnregisterEndpointNotificationCallback;
}
