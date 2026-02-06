namespace Spice86.Core.Backend.Audio.CrossPlatform.Sdl;

using System;
using System.Runtime.InteropServices;

/// <summary>
/// SDL audio format flags.
/// </summary>
[Flags]
internal enum SdlAudioFormat : ushort {
    U8 = 0x0008,
    S8 = 0x8008,
    U16Lsb = 0x0010,
    S16Lsb = 0x8010,
    U16Msb = 0x1010,
    S16Msb = 0x9010,
    S32Lsb = 0x8020,
    S32Msb = 0x9020,
    F32Lsb = 0x8120,
    F32Msb = 0x9120,

    // Convenience aliases
    F32 = F32Lsb,
    S16 = S16Lsb,
    S32 = S32Lsb
}

/// <summary>
/// SDL_AudioSpec structure for audio device configuration.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SdlAudioSpec {
    public int Freq;
    public SdlAudioFormat Format;
    public byte Channels;
    public byte Silence;
    public ushort Samples;
    public ushort Padding;
    public uint Size;
    public IntPtr Callback;
    public IntPtr Userdata;
}

/// <summary>
/// SDL audio callback delegate.
/// </summary>
/// <param name="userdata">User data pointer.</param>
/// <param name="stream">Audio buffer to fill.</param>
/// <param name="len">Length of the buffer in bytes.</param>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void SdlAudioCallback(IntPtr userdata, IntPtr stream, int len);

// Delegate types for SDL functions
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int SdlInitDelegate(uint flags);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void SdlQuitSubSystemDelegate(uint flags);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void SdlQuitDelegate();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate IntPtr SdlGetErrorDelegate();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate uint SdlOpenAudioDeviceDelegate(IntPtr device, int isCapture, ref SdlAudioSpec desired, out SdlAudioSpec obtained, int allowedChanges);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void SdlCloseAudioDeviceDelegate(uint deviceId);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void SdlPauseAudioDeviceDelegate(uint deviceId, int pauseOn);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void SdlLockAudioDeviceDelegate(uint deviceId);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void SdlUnlockAudioDeviceDelegate(uint deviceId);

/// <summary>
/// P/Invoke bindings to SDL2 audio subsystem.
/// </summary>
internal static class SdlNativeMethods {
    private const string SdlLibWindows = "SDL2.dll";
    private const string SdlLibLinux = "libSDL2-2.0.so.0";
    private const string SdlLibMacOS = "libSDL2.dylib";

    // SDL initialization flags
    public const uint SdlInitAudio = 0x00000010;

    // SDL audio device flags
    public const int SdlAudioAllowFrequencyChange = 0x00000001;
    public const int SdlAudioAllowFormatChange = 0x00000002;
    public const int SdlAudioAllowChannelsChange = 0x00000004;
    public const int SdlAudioAllowSamplesChange = 0x00000008;
    public const int SdlAudioAllowAnyChange = SdlAudioAllowFrequencyChange | SdlAudioAllowFormatChange |
                                               SdlAudioAllowChannelsChange | SdlAudioAllowSamplesChange;

    // Determine library name based on platform
    private static string GetLibraryName() {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            return SdlLibWindows;
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
            return SdlLibLinux;
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            return SdlLibMacOS;
        }
        return SdlLibLinux; // Default to Linux
    }

    private static IntPtr _sdlHandle;
    private static bool _initialized;

    // Delegates loaded at runtime
    private static SdlInitDelegate? _sdlInit;
    private static SdlQuitSubSystemDelegate? _sdlQuitSubSystem;
    private static SdlQuitDelegate? _sdlQuit;
    private static SdlGetErrorDelegate? _sdlGetError;
    private static SdlOpenAudioDeviceDelegate? _sdlOpenAudioDevice;
    private static SdlCloseAudioDeviceDelegate? _sdlCloseAudioDevice;
    private static SdlPauseAudioDeviceDelegate? _sdlPauseAudioDevice;
    private static SdlLockAudioDeviceDelegate? _sdlLockAudioDevice;
    private static SdlUnlockAudioDeviceDelegate? _sdlUnlockAudioDevice;

    public static bool Initialize() {
        if (_initialized) {
            return true;
        }

        try {
            string libName = GetLibraryName();
            _sdlHandle = NativeLibrary.Load(libName);

            _sdlInit = Marshal.GetDelegateForFunctionPointer<SdlInitDelegate>(
                NativeLibrary.GetExport(_sdlHandle, "SDL_Init"));
            _sdlQuitSubSystem = Marshal.GetDelegateForFunctionPointer<SdlQuitSubSystemDelegate>(
                NativeLibrary.GetExport(_sdlHandle, "SDL_QuitSubSystem"));
            _sdlQuit = Marshal.GetDelegateForFunctionPointer<SdlQuitDelegate>(
                NativeLibrary.GetExport(_sdlHandle, "SDL_Quit"));
            _sdlGetError = Marshal.GetDelegateForFunctionPointer<SdlGetErrorDelegate>(
                NativeLibrary.GetExport(_sdlHandle, "SDL_GetError"));
            _sdlOpenAudioDevice = Marshal.GetDelegateForFunctionPointer<SdlOpenAudioDeviceDelegate>(
                NativeLibrary.GetExport(_sdlHandle, "SDL_OpenAudioDevice"));
            _sdlCloseAudioDevice = Marshal.GetDelegateForFunctionPointer<SdlCloseAudioDeviceDelegate>(
                NativeLibrary.GetExport(_sdlHandle, "SDL_CloseAudioDevice"));
            _sdlPauseAudioDevice = Marshal.GetDelegateForFunctionPointer<SdlPauseAudioDeviceDelegate>(
                NativeLibrary.GetExport(_sdlHandle, "SDL_PauseAudioDevice"));
            _sdlLockAudioDevice = Marshal.GetDelegateForFunctionPointer<SdlLockAudioDeviceDelegate>(
                NativeLibrary.GetExport(_sdlHandle, "SDL_LockAudioDevice"));
            _sdlUnlockAudioDevice = Marshal.GetDelegateForFunctionPointer<SdlUnlockAudioDeviceDelegate>(
                NativeLibrary.GetExport(_sdlHandle, "SDL_UnlockAudioDevice"));

            _initialized = true;
            return true;
        } catch (DllNotFoundException) {
            return false;
        } catch (EntryPointNotFoundException) {
            return false;
        }
    }

    public static int SdlInit(uint flags) {
        if (!_initialized && !Initialize()) {
            return -1;
        }
        return _sdlInit!(flags);
    }

    public static void SdlQuitSubSystem(uint flags) {
        if (_initialized && _sdlQuitSubSystem != null) {
            _sdlQuitSubSystem(flags);
        }
    }

    public static void SdlQuit() {
        if (_initialized && _sdlQuit != null) {
            _sdlQuit();
        }
    }

    public static string? SdlGetError() {
        if (!_initialized || _sdlGetError == null) {
            return "SDL not initialized";
        }
        IntPtr errorPtr = _sdlGetError();
        return Marshal.PtrToStringAnsi(errorPtr);
    }

    public static uint SdlOpenAudioDevice(string? device, int isCapture, ref SdlAudioSpec desired, out SdlAudioSpec obtained, int allowedChanges) {
        if (!_initialized || _sdlOpenAudioDevice == null) {
            obtained = default;
            return 0;
        }
        IntPtr devicePtr = device != null ? Marshal.StringToHGlobalAnsi(device) : IntPtr.Zero;
        try {
            return _sdlOpenAudioDevice(devicePtr, isCapture, ref desired, out obtained, allowedChanges);
        } finally {
            if (devicePtr != IntPtr.Zero) {
                Marshal.FreeHGlobal(devicePtr);
            }
        }
    }

    public static void SdlCloseAudioDevice(uint deviceId) {
        if (_initialized && deviceId != 0 && _sdlCloseAudioDevice != null) {
            _sdlCloseAudioDevice(deviceId);
        }
    }

    public static void SdlPauseAudioDevice(uint deviceId, int pauseOn) {
        if (_initialized && deviceId != 0 && _sdlPauseAudioDevice != null) {
            _sdlPauseAudioDevice(deviceId, pauseOn);
        }
    }

    public static void SdlLockAudioDevice(uint deviceId) {
        if (_initialized && deviceId != 0 && _sdlLockAudioDevice != null) {
            _sdlLockAudioDevice(deviceId);
        }
    }

    public static void SdlUnlockAudioDevice(uint deviceId) {
        if (_initialized && deviceId != 0 && _sdlUnlockAudioDevice != null) {
            _sdlUnlockAudioDevice(deviceId);
        }
    }

    public static void Shutdown() {
        if (_initialized && _sdlHandle != IntPtr.Zero) {
            NativeLibrary.Free(_sdlHandle);
            _sdlHandle = IntPtr.Zero;
            _initialized = false;
        }
    }
}
