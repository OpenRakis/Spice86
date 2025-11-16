namespace Bufdio.Spice86.Utilities.Extensions;

using Bufdio.Spice86.Bindings.PortAudio;
using Bufdio.Spice86.Bindings.PortAudio.Structs;
using Bufdio.Spice86.Exceptions;

using System.Runtime.InteropServices;

/// <summary>
/// Extension methods for PortAudio error handling and device information retrieval.
/// </summary>
internal static class PortAudioExtensions {
    /// <summary>
    /// Determines whether the specified PortAudio error code represents an error.
    /// </summary>
    /// <param name="code">The PortAudio error code to check.</param>
    /// <returns><c>true</c> if the code represents an error (negative value); otherwise, <c>false</c>.</returns>
    public static bool PaIsError(this int code) {
        return code < 0;
    }

    /// <summary>
    /// Guards against PortAudio errors by throwing an exception if the code represents an error.
    /// </summary>
    /// <param name="code">The PortAudio error code to check.</param>
    /// <returns>The original code if it does not represent an error.</returns>
    /// <exception cref="PortAudioException">Thrown when the code represents an error.</exception>
    public static int PaGuard(this int code) {
        if (!code.PaIsError()) {
            return code;
        }

        throw new PortAudioException(code);
    }

    /// <summary>
    /// Converts a PortAudio error code to its textual representation.
    /// </summary>
    /// <param name="code">The PortAudio error code to convert.</param>
    /// <returns>A string describing the error, or <c>null</c> if the conversion fails.</returns>
    public static string? PaErrorToText(this int code) {
        nint ptr = NativeMethods.PortAudioGetErrorText(code);
        return Marshal.PtrToStringAnsi(ptr);
    }

    /// <summary>
    /// Retrieves the device information for the specified PortAudio device index.
    /// </summary>
    /// <param name="device">The device index.</param>
    /// <returns>The device information structure.</returns>
    public static PaDeviceInfo PaGetPaDeviceInfo(this int device) {
        nint ptr = NativeMethods.PortAudioGetDeviceInfo(device);
        return Marshal.PtrToStructure<PaDeviceInfo>(ptr);
    }

    /// <summary>
    /// Converts a PortAudio device information structure to an AudioDevice object.
    /// </summary>
    /// <param name="device">The PortAudio device information.</param>
    /// <param name="deviceIndex">The device index.</param>
    /// <returns>An AudioDevice object containing the device information.</returns>
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