namespace Spice86.Backend.Audio.OpenAl.DirectSound.Interop;

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[SupportedOSPlatform(("windows"))]
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct DirectSoundBuffer8V
{
    public delegate* unmanaged[Stdcall]<DirectSoundBuffer8Inst*, Guid*, void**, uint> QueryInterface;
    public delegate* unmanaged[Stdcall]<DirectSoundBuffer8Inst*, uint> AddRef;
    public delegate* unmanaged[Stdcall]<DirectSoundBuffer8Inst*, uint> Release;

    public delegate* unmanaged[Stdcall]<DirectSoundBuffer8Inst*, DSBCAPS*, uint> GetCaps;
    public delegate* unmanaged[Stdcall]<DirectSoundBuffer8Inst*, uint*, uint*, uint> GetCurrentPosition;
    public IntPtr GetFormat;
    public delegate* unmanaged[Stdcall]<DirectSoundBuffer8Inst*, int*, uint> GetVolume;
    public delegate* unmanaged[Stdcall]<DirectSoundBuffer8Inst*, int*, uint> GetPan;
    public delegate* unmanaged[Stdcall]<DirectSoundBuffer8Inst*, uint*, uint> GetFrequency;
    public delegate* unmanaged[Stdcall]<DirectSoundBuffer8Inst*, uint*, uint> GetStatus;
    public IntPtr Initialize;
    public delegate* unmanaged[Stdcall]<DirectSoundBuffer8Inst*, uint, uint, void**, uint*, void**, uint*, uint, uint> Lock;
    public delegate* unmanaged[Stdcall]<DirectSoundBuffer8Inst*, uint, uint, uint, uint> Play;
    public delegate* unmanaged[Stdcall]<DirectSoundBuffer8Inst*, uint, uint> SetCurrentPosition;
    public IntPtr SetFormat;
    public delegate* unmanaged[Stdcall]<DirectSoundBuffer8Inst*, int, uint> SetVolume;
    public delegate* unmanaged[Stdcall]<DirectSoundBuffer8Inst*, int, uint> SetPan;
    public delegate* unmanaged[Stdcall]<DirectSoundBuffer8Inst*, uint, uint> SetFrequency;
    public delegate* unmanaged[Stdcall]<DirectSoundBuffer8Inst*, uint> Stop;
    public delegate* unmanaged[Stdcall]<DirectSoundBuffer8Inst*, void*, uint, void*, uint, uint> Unlock;
    public IntPtr Restore;
    public IntPtr SetFX;
    public IntPtr AcquireResources;
    public IntPtr GetObjectInPath;
}
