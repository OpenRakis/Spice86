namespace Spice86.Core.Backend.Audio.CrossPlatform.Wasapi;

using System;
using System.Runtime.InteropServices;

/// <summary>
/// IMMDeviceEnumerator COM interface.
/// </summary>
[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator {
    int EnumAudioEndpoints(DataFlow dataFlow, uint stateMask, out IntPtr devices);
    int GetDefaultAudioEndpoint(DataFlow dataFlow, Role role, out IMMDevice device);
    int GetDevice(string id, out IMMDevice device);
    int RegisterEndpointNotificationCallback(IntPtr client);
    int UnregisterEndpointNotificationCallback(IntPtr client);
}

/// <summary>
/// IMMDevice COM interface.
/// </summary>
[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice {
    int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
    int OpenPropertyStore(int stgmAccess, out IntPtr properties);
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    int GetState(out uint state);
}

/// <summary>
/// IAudioClient COM interface.
/// </summary>
[ComImport]
[Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient {
    int Initialize(
        AudioClientShareMode shareMode,
        AudioClientStreamFlags streamFlags,
        long bufferDuration,
        long periodicity,
        IntPtr format,
        IntPtr audioSessionGuid);

    int GetBufferSize(out uint bufferFrameCount);
    int GetStreamLatency(out long latency);
    int GetCurrentPadding(out uint paddingFrameCount);

    int IsFormatSupported(
        AudioClientShareMode shareMode,
        IntPtr format,
        out IntPtr closestMatch);

    int GetMixFormat(out IntPtr format);
    int GetDevicePeriod(out long defaultDevicePeriod, out long minimumDevicePeriod);
    int Start();
    int Stop();
    int Reset();
    int SetEventHandle(IntPtr eventHandle);

    int GetService(
        ref Guid iid,
        [MarshalAs(UnmanagedType.IUnknown)] out object service);
}

/// <summary>
/// IAudioRenderClient COM interface.
/// </summary>
[ComImport]
[Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioRenderClient {
    int GetBuffer(uint numFramesRequested, out IntPtr dataPtr);
    int ReleaseBuffer(uint numFramesWritten, uint flags);
}
