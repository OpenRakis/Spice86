namespace Bufdio.Spice86.Bindings.PortAudio;

using System;
using System.Runtime.InteropServices;

/// <summary>
/// A static class that contains P/Invoke wrappers for the PortAudio library.
/// </summary>
/// <remarks>
/// This class is a wrapper around the PortAudio library. PortAudio is a free, cross-platform, open-source, audio I/O library.
/// </remarks>
internal static partial class PaBinding {
    private static Initialize _initialize = null!;
    private static Terminate _terminate = null!;
    private static GetVersionInfo _getVersionInfo = null!;
    private static GetErrorText _getErrorText = null!;
    private static GetDefaultOutputDevice _getDefaultOutputDevice = null!;
    private static GetDeviceInfo _getDeviceInfo = null!;
    private static GetDeviceCount _getDeviceCount = null!;
    private static OpenStream _openStream = null!;
    private static StartStream _startStream = null!;
    private static WriteStream _writeStream = null!;
    private static AbortStream _abortStream = null!;
    private static CloseStream _closeStream = null!;
    
    /// <summary>
    /// A callback function that is used by a stream to provide or consume audio data in real time.
    /// </summary>
    /// <param name="input">A buffer containing the input samples.</param>
    /// <param name="output">A buffer where the output samples should be placed.</param>
    /// <param name="frameCount">The number of frames to be processed by the callback.</param>
    /// <param name="timeInfo">A structure containing timestamps representing the capture time of the first sample in the input buffer, and the time of the deadline for the first sample in the output buffer.</param>
    /// <param name="statusFlags">Flags indicating whether input and/or output underflow and/or overflow conditions occurred.</param>
    /// <param name="userData">A pointer to user-defined data.</param>
    /// <returns>A value indicating whether the stream should continue calling the callback function.</returns>
    public unsafe delegate PaStreamCallbackResult PaStreamCallback(
        void* input,
        void* output,
        long frameCount,
        IntPtr timeInfo,
        PaStreamCallbackFlags statusFlags,
        void* userData
    );
    
    /// <summary>
    /// A delegate representing the PortAudio initialization function.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Initialize();

    /// <summary>
    /// A delegate representing the PortAudio termination function.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Terminate();

    /// <summary>
    /// A delegate representing the PortAudio function for getting the library version information.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetVersionInfo();

    /// <summary>
    /// A delegate representing the PortAudio function for getting error messages.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetErrorText(int code);

    /// <summary>
    /// A delegate representing the PortAudio function for getting the default output device.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetDefaultOutputDevice();

    /// <summary>
    /// A delegate representing the PortAudio function for getting information about a specific device.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetDeviceInfo(int device);

    /// <summary>
    /// A delegate representing the PortAudio function for getting the number of available devices.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetDeviceCount();

    /// <summary>
    /// A delegate representing the PortAudio function for opening a new stream.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int OpenStream(
        IntPtr stream,
        IntPtr inputParameters,
        IntPtr outputParameters,
        double sampleRate,
        long framesPerBuffer,
        PaStreamFlags streamFlags,
        PaStreamCallback? streamCallback,
        IntPtr userData);

    /// <summary>
    /// A delegate representing the PortAudio function for starting an open stream.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int StartStream(IntPtr stream);

    /// <summary>
    /// A delegate representing the PortAudio function for writing to an open stream.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int WriteStream(IntPtr stream, IntPtr buffer, long frames);

    /// <summary>
    /// A delegate representing the PortAudio function for aborting an open stream.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int AbortStream(IntPtr stream);

    /// <summary>
    /// A delegate representing the PortAudio function for closing an open stream.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CloseStream(IntPtr stream);
}
