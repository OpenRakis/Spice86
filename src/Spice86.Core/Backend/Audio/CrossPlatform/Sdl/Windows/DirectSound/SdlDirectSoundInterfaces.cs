namespace Spice86.Core.Backend.Audio.CrossPlatform.Sdl.Windows.DirectSound;

using System;
using System.Runtime.InteropServices;

[ComImport]
[Guid("C50A7E93-F395-4834-9EF6-7FA99DE50966")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDirectSound8 {
    int CreateSoundBuffer(ref DsBufferDesc bufferDesc, out IDirectSoundBuffer soundBuffer, IntPtr outer);
    int GetCaps(IntPtr caps);
    int DuplicateSoundBuffer(IntPtr original, out IDirectSoundBuffer duplicate);
    int SetCooperativeLevel(IntPtr windowHandle, uint level);
    int Compact();
    int GetSpeakerConfig(out uint speakerConfig);
    int SetSpeakerConfig(uint speakerConfig);
    int Initialize(IntPtr guid);
}

[ComImport]
[Guid("279AFA85-4981-11CE-A521-0020AF0BE560")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDirectSoundBuffer {
    int GetCaps(IntPtr caps);
    int GetCurrentPosition(out uint playCursor, out uint writeCursor);
    int GetFormat(IntPtr format, uint sizeAllocated, out uint sizeWritten);
    int GetVolume(out int volume);
    int GetPan(out int pan);
    int GetFrequency(out uint frequency);
    int GetStatus(out uint status);
    int Initialize(IDirectSound8 directSound, ref DsBufferDesc bufferDesc);
    int Lock(uint offset, uint bytes, out IntPtr audioPtr1, out uint audioBytes1, out IntPtr audioPtr2, out uint audioBytes2, uint flags);
    int Play(uint reserved1, uint priority, uint flags);
    int SetCurrentPosition(uint newPosition);
    int SetFormat(IntPtr format);
    int SetVolume(int volume);
    int SetPan(int pan);
    int SetFrequency(uint frequency);
    int Stop();
    int Unlock(IntPtr audioPtr1, uint audioBytes1, IntPtr audioPtr2, uint audioBytes2);
    int Restore();
}

internal static partial class SdlDirectSoundNative {
    [DllImport("dsound.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int DirectSoundCreate8(IntPtr deviceGuid, out IDirectSound8 directSound, IntPtr outer);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetDesktopWindow();
}
