namespace Spice86.Core.Backend.Audio.OpenAl.DirectSound.Interop;

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[SupportedOSPlatform("windows")]
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct DirectSound8V {
    public delegate* unmanaged[Stdcall]<DirectSound8Inst*, Guid*, void**, uint> QueryInterface;
    public delegate* unmanaged[Stdcall]<DirectSound8Inst*, uint> AddRef;
    public delegate* unmanaged[Stdcall]<DirectSound8Inst*, uint> Release;

    public delegate* unmanaged[Stdcall]<DirectSound8Inst*, DSBUFFERDESC*, void*, DirectSoundBuffer8Inst*, uint> CreateBuffer;
    public delegate* unmanaged[Stdcall]<DirectSound8Inst*, DSCAPS*, uint> GetCaps;
    public delegate* unmanaged[Stdcall]<DirectSound8Inst*, void*, void**, uint> DuplicateSoundBuffer;
    public delegate* unmanaged[Stdcall]<DirectSound8Inst*, IntPtr, uint, uint> SetCooperativeLevel;

    public IntPtr Compact;
    public IntPtr GetSpeakerConfig;
    public IntPtr SetSpeakerConfig;
    public IntPtr Initialize;
    public IntPtr VerifyCertification;
}
