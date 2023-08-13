namespace Bufdio.Spice86.Utilities.Extensions;
using System.Runtime.InteropServices;

using Bufdio.Spice86.Bindings.PortAudio;
using Bufdio.Spice86.Exceptions;

internal static class PortAudioExtensions {
    public static bool PaIsError(this int code) {
        return code < 0;
    }

    public static int PaGuard(this int code) {
        if (!code.PaIsError())
        {
            return code;
        }

        throw new PortAudioException(code);
    }

    public static string? PaErrorToText(this int code) {
        nint ptr = 0;
        if (PlatformInfo.IsWindows) {
            ptr = PaBinding.Windows.Pa_GetErrorText(code);
        } else if (PlatformInfo.IsLinux) {
            ptr = PaBinding.Linux.Pa_GetErrorText(code);
        } else if (PlatformInfo.IsOSX) {
            ptr = PaBinding.OSX.Pa_GetErrorText(code);
        }
        return Marshal.PtrToStringAnsi(ptr);
    }

    public static PaBinding.PaDeviceInfo PaGetPaDeviceInfo(this int device) {
        nint ptr = 0;
        if (PlatformInfo.IsWindows) {
            ptr = PaBinding.Windows.Pa_GetDeviceInfo(device);
        } else if (PlatformInfo.IsLinux) {
            ptr = PaBinding.Linux.Pa_GetDeviceInfo(device);
        } else if (PlatformInfo.IsOSX) {
            ptr = PaBinding.OSX.Pa_GetDeviceInfo(device);
        }
        return Marshal.PtrToStructure<PaBinding.PaDeviceInfo>(ptr);
    }

    public static AudioDevice PaToAudioDevice(this PaBinding.PaDeviceInfo device, int deviceIndex) {
        return new AudioDevice(
            deviceIndex,
            device.name,
            device.maxOutputChannels,
            device.defaultLowOutputLatency,
            device.defaultHighOutputLatency,
            (int)device.defaultSampleRate);
    }
}
