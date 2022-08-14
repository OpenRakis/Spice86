using System.Runtime.InteropServices;
using Bufdio.Bindings.PortAudio;
using Bufdio.Exceptions;

namespace Bufdio.Utilities.Extensions;

internal static class PortAudioExtensions
{
    public static bool PaIsError(this int code)
    {
        return code < 0;
    }

    public static int PaGuard(this int code)
    {
        if (!code.PaIsError())
        {
            return code;
        }

        throw new PortAudioException(code);
    }

    public static string PaErrorToText(this int code)
    {
        return Marshal.PtrToStringAnsi(PaBinding.Pa_GetErrorText(code));
    }

    public static PaBinding.PaDeviceInfo PaGetPaDeviceInfo(this int device)
    {
        return Marshal.PtrToStructure<PaBinding.PaDeviceInfo>(PaBinding.Pa_GetDeviceInfo(device));
    }

    public static AudioDevice PaToAudioDevice(this PaBinding.PaDeviceInfo device, int deviceIndex)
    {
        return new AudioDevice(
            deviceIndex,
            device.name,
            device.maxOutputChannels,
            device.defaultLowOutputLatency,
            device.defaultHighOutputLatency,
            (int)device.defaultSampleRate);
    }
}
