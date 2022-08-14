using System;
using System.Runtime.InteropServices;

namespace Bufdio.Bindings.PortAudio;

internal static partial class PaBinding
{
    private static Initialize _initialize;
    private static Terminate _terminate;
    private static GetVersionInfo _getVersionInfo;
    private static GetErrorText _getErrorText;
    private static GetDefaultOutputDevice _getDefaultOutputDevice;
    private static GetDeviceInfo _getDeviceInfo;
    private static GetDeviceCount _getDeviceCount;
    private static OpenStream _openStream;
    private static StartStream _startStream;
    private static WriteStream _writeStream;
    private static AbortStream _abortStream;
    private static CloseStream _closeStream;

    public unsafe delegate PaStreamCallbackResult PaStreamCallback(
        void* input,
        void* output,
        long frameCount,
        IntPtr timeInfo,
        PaStreamCallbackFlags statusFlags,
        void* userData
    );

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Initialize();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Terminate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetVersionInfo();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetErrorText(int code);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetDefaultOutputDevice();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetDeviceInfo(int device);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetDeviceCount();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int OpenStream(
        IntPtr stream,
        IntPtr inputParameters,
        IntPtr outputParameters,
        double sampleRate,
        long framesPerBuffer,
        PaStreamFlags streamFlags,
        PaStreamCallback streamCallback,
        IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int StartStream(IntPtr stream);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int WriteStream(IntPtr stream, IntPtr buffer, long frames);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int AbortStream(IntPtr stream);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CloseStream(IntPtr stream);
}
