namespace Bufdio.Spice86.Utilities.Extensions;

using Bufdio.Spice86.Bindings.PortAudio;
using Bufdio.Spice86.Bindings.PortAudio.Structs;
using Bufdio.Spice86.Exceptions;

using System.Runtime.InteropServices;

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
        nint ptr = NativeMethods.PortAudioGetErrorText(code);
        return Marshal.PtrToStringAnsi(ptr);
    }

    public static PaDeviceInfo PaGetPaDeviceInfo(this int device) {
        nint ptr = NativeMethods.PortAudioGetDeviceInfo(device);
        return Marshal.PtrToStructure<PaDeviceInfo>(ptr);
    }

    public static AudioDevice PaToAudioDevice(this PaDeviceInfo device, int deviceIndex) {
        return new AudioDevice(
            deviceIndex,
            device.name,
            device.maxOutputChannels,
            device.defaultLowOutputLatency,
            device.defaultHighOutputLatency,
            (int)device.defaultSampleRate);
    }
}
