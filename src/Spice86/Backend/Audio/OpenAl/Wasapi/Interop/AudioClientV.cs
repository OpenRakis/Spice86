namespace Spice86.Backend.Audio.OpenAl.Wasapi.Interop;

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[SupportedOSPlatform(("windows"))]
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct AudioClientV
{
    public delegate* unmanaged[Stdcall]<AudioClientInst*, Guid*, void**, uint> QueryInterface;
    public delegate* unmanaged[Stdcall]<AudioClientInst*, uint> AddRef;
    public delegate* unmanaged[Stdcall]<AudioClientInst*, uint> Release;

    public delegate* unmanaged[Stdcall]<AudioClientInst*, uint, uint, long, long, WAVEFORMATEX*, Guid*, uint> Initialize;
    public delegate* unmanaged[Stdcall]<AudioClientInst*, uint*, uint> GetBufferSize;
    public delegate* unmanaged[Stdcall]<AudioClientInst*, long*, uint> GetStreamLatency;
    public delegate* unmanaged[Stdcall]<AudioClientInst*, uint*, uint> GetCurrentPadding;
    public delegate* unmanaged[Stdcall]<AudioClientInst*, uint, WAVEFORMATEX*, WAVEFORMATEX**, uint> IsFormatSupported;
    public delegate* unmanaged[Stdcall]<AudioClientInst*, WAVEFORMATEX**, uint> GetMixFormat;
    public delegate* unmanaged[Stdcall]<AudioClientInst*, long*, long*, uint> GetDevicePeriod;
    public delegate* unmanaged[Stdcall]<AudioClientInst*, uint> Start;
    public delegate* unmanaged[Stdcall]<AudioClientInst*, uint> Stop;
    public delegate* unmanaged[Stdcall]<AudioClientInst*, uint> Reset;
    public delegate* unmanaged[Stdcall]<AudioClientInst*, IntPtr, uint> SetEventHandle;
    public delegate* unmanaged[Stdcall]<AudioClientInst*, Guid*, void**, uint> GetService;
}
