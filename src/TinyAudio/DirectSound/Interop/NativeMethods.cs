namespace TinyAudio.DirectSound.Interop;

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[SupportedOSPlatform("windows")]
internal static class NativeMethods
{
    [DllImport("dsound.dll", CallingConvention = CallingConvention.Winapi)]
    public static extern unsafe uint DirectSoundCreate8(void* lpcGuidDevice, out DirectSound8Inst* ppDS8, void* pUnkOuter);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetConsoleWindow();
}
