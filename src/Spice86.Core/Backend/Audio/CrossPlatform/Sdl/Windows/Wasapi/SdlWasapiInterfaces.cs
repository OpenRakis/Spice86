namespace Spice86.Core.Backend.Audio.CrossPlatform.Sdl.Windows.Wasapi;

using System;
using System.Runtime.InteropServices;

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator {
    int EnumAudioEndpoints(DataFlow dataFlow, DeviceState stateMask, out IMMDeviceCollection devices);

    int GetDefaultAudioEndpoint(DataFlow dataFlow, Role role, out IMMDevice device);

    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice device);

    int RegisterEndpointNotificationCallback(IMMNotificationClient client);

    int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice {
    int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.Interface)] out object interfacePointer);

    int OpenPropertyStore(int stgmAccess, out IPropertyStore properties);

    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

    int GetState(out DeviceState state);
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-C0A0B7C9D5CC")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection {
    int GetCount(out uint count);

    int Item(uint index, out IMMDevice device);
}

[ComImport]
[Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMNotificationClient {
    int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, DeviceState newState);

    int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    int OnDefaultDeviceChanged(DataFlow flow, Role role, [MarshalAs(UnmanagedType.LPWStr)] string defaultDeviceId);

    int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PropertyKey key);
}

[ComImport]
[Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore {
    int GetCount(out uint propertyCount);

    int GetAt(uint propertyIndex, out PropertyKey key);

    int GetValue(ref PropertyKey key, out PropVariant value);

    int SetValue(ref PropertyKey key, ref PropVariant value);

    int Commit();
}

[ComImport]
[Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient {
    int Initialize(AudioClientShareMode shareMode, AudioClientStreamFlags streamFlags,
        long hnsBufferDuration, long hnsPeriodicity, IntPtr format, IntPtr audioSessionGuid);

    int GetBufferSize(out uint bufferFrames);

    int GetStreamLatency(out long latency);

    int GetCurrentPadding(out uint paddingFrames);

    int IsFormatSupported(AudioClientShareMode shareMode, IntPtr format, out IntPtr closestMatch);

    int GetMixFormat(out IntPtr deviceFormat);

    int GetDevicePeriod(out long defaultDevicePeriod, out long minimumDevicePeriod);

    int Start();

    int Stop();

    int Reset();

    int SetEventHandle(IntPtr eventHandle);

    int GetService(ref Guid iid, out IntPtr service);
}

[ComImport]
[Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioRenderClient {
    int GetBuffer(uint numFramesRequested, out IntPtr dataBuffer);

    int ReleaseBuffer(uint numFramesWritten, uint flags);
}

internal enum DataFlow {
    Render = 0,
    Capture = 1,
    All = 2
}

internal enum Role {
    Console = 0,
    Multimedia = 1,
    Communications = 2
}

[Flags]
internal enum DeviceState {
    Active = 0x00000001,
    Disabled = 0x00000002,
    NotPresent = 0x00000004,
    Unplugged = 0x00000008,
    All = 0x0000000F
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey {
    public Guid FormatId;
    public uint PropertyId;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant {
    public ushort ValueType;
    public ushort Reserved1;
    public ushort Reserved2;
    public ushort Reserved3;
    public IntPtr Value;
    public int Value2;
}
