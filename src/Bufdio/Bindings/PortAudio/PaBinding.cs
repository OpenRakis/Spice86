using System;
using Bufdio.Utilities;

namespace Bufdio.Bindings.PortAudio;

internal static partial class PaBinding
{
    public static void InitializeBindings(LibraryLoader loader)
    {
        _initialize = loader.LoadFunc<Initialize>(nameof(Pa_Initialize));
        _terminate = loader.LoadFunc<Terminate>(nameof(Pa_Terminate));

        _getVersionInfo = loader.LoadFunc<GetVersionInfo>(nameof(Pa_GetVersionInfo));
        _getErrorText = loader.LoadFunc<GetErrorText>(nameof(Pa_GetErrorText));

        _getDefaultOutputDevice = loader.LoadFunc<GetDefaultOutputDevice>(nameof(Pa_GetDefaultOutputDevice));
        _getDeviceInfo = loader.LoadFunc<GetDeviceInfo>(nameof(Pa_GetDeviceInfo));
        _getDeviceCount = loader.LoadFunc<GetDeviceCount>(nameof(Pa_GetDeviceCount));

        _openStream = loader.LoadFunc<OpenStream>(nameof(Pa_OpenStream));
        _writeStream = loader.LoadFunc<WriteStream>(nameof(Pa_WriteStream));
        _startStream = loader.LoadFunc<StartStream>(nameof(Pa_StartStream));
        _abortStream = loader.LoadFunc<AbortStream>(nameof(Pa_AbortStream));
        _closeStream = loader.LoadFunc<CloseStream>(nameof(Pa_CloseStream));
    }

    public static int Pa_Initialize()
    {
        return _initialize();
    }

    public static int Pa_Terminate()
    {
        return _terminate();
    }

    public static IntPtr Pa_GetVersionInfo()
    {
        return _getVersionInfo();
    }

    public static IntPtr Pa_GetErrorText(int code)
    {
        return _getErrorText(code);
    }

    public static int Pa_GetDefaultOutputDevice()
    {
        return _getDefaultOutputDevice();
    }

    public static IntPtr Pa_GetDeviceInfo(int device)
    {
        return _getDeviceInfo(device);
    }

    public static int Pa_GetDeviceCount()
    {
        return _getDeviceCount();
    }

    public static int Pa_OpenStream(
        IntPtr stream,
        IntPtr inputParameters,
        IntPtr outputParameters,
        double sampleRate,
        long framesPerBuffer,
        PaStreamFlags streamFlags,
        PaStreamCallback streamCallback,
        IntPtr userData)
    {
        return _openStream(
            stream,
            inputParameters,
            outputParameters,
            sampleRate,
            framesPerBuffer,
            streamFlags,
            streamCallback,
            userData
        );
    }

    public static int Pa_StartStream(IntPtr stream)
    {
        return _startStream(stream);
    }

    public static int Pa_WriteStream(IntPtr stream, IntPtr buffer, long frames)
    {
        return _writeStream(stream, buffer, frames);
    }

    public static int Pa_AbortStream(IntPtr stream)
    {
        return _abortStream(stream);
    }

    public static int Pa_CloseStream(IntPtr stream)
    {
        return _closeStream(stream);
    }
}
