namespace Bufdio.Spice86.Bindings.PortAudio;

using System;
using System.Runtime.InteropServices;

using Bufdio.Spice86.Bindings.PortAudio.Enums;
using Bufdio.Spice86.Utilities;

using System.Runtime.Versioning;

/// <summary>
/// Platform Invoke bindings to the PortAudio ABI
/// </summary>
internal static partial class NativeMethods {
    public static int PortAudioInitialize() => _bindings.Initialize();

    public static int PortAudioGetDeviceCount() => _bindings.GetDeviceCount();

    public static int PortAudioGetDefaultOutputDevice() => _bindings.GetDefaultOutputDevice();

    public static int PortAudioStartStream(IntPtr stream) => _bindings.StartStream(stream);

    public static int PortAudioOpenStream(IntPtr stream, IntPtr inputParameters, IntPtr outputParameters, double sampleRate, long framesPerBuffer, PaStreamFlags streamFlags, PaStreamCallback? streamCallback, IntPtr userData) => _bindings.OpenStream(stream, inputParameters,
        outputParameters, sampleRate, framesPerBuffer, streamFlags, streamCallback, userData);

    public static int PortAudioWriteStream(IntPtr stream, IntPtr buffer, long frames) => _bindings.WriteStream(stream, buffer, frames);

    public static int PortAudioCloseStream(IntPtr stream) => _bindings.CloseStream(stream);

    public static IntPtr PortAudioGetErrorText(int code) => _bindings.GetErrorText(code);

    public static IntPtr PortAudioGetDeviceInfo(int device) => _bindings.GetDeviceInfo(device);

    public static int PortAudioAbortStream(IntPtr stream) => _bindings.AbortStream(stream);

    private interface INativeBindings {
        int Initialize();
        int Terminate();
        IntPtr GetVersionInfo();
        IntPtr GetErrorText(int code);
        int GetDefaultOutputDevice();
        IntPtr GetDeviceInfo(int device);
        int GetDeviceCount();
        int OpenStream(IntPtr stream, IntPtr inputParameters, IntPtr outputParameters, double sampleRate, long framesPerBuffer, PaStreamFlags streamFlags, PaStreamCallback? streamCallback, IntPtr userData);
        int StartStream(IntPtr stream);
        int WriteStream(IntPtr stream, IntPtr buffer, long frames);
        int AbortStream(IntPtr stream);
        int CloseStream(IntPtr stream);
    }

    private static readonly INativeBindings _bindings;

    static NativeMethods() {
        if (OperatingSystem.IsWindows()) {
            _bindings = new Windows();
        } else if (OperatingSystem.IsLinux()) {
            _bindings = new Linux();
        } else if (OperatingSystem.IsMacOS()) {
            _bindings = new MacOS();
        } else {
            throw new PlatformNotSupportedException();
        }
    }

    public static string GetPortAudioLibName() {
        if (PlatformInfo.IsWindows) {
            return "libportaudio.dll";
        } else if (PlatformInfo.IsLinux) {
            return "libportaudio.so.2";
        } else if (PlatformInfo.IsOSX) {
            return "libportaudio.2.dylib";
        } else {
            throw new PlatformNotSupportedException();
        }
    }

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
    internal unsafe delegate PaStreamCallbackResult PaStreamCallback(
        void* input,
        void* output,
        long frameCount,
        IntPtr timeInfo,
        PaStreamCallbackFlags statusFlags,
        void* userData
    );

    [SupportedOSPlatform("Windows")]
    private partial class Windows : INativeBindings {
        [LibraryImport("libportaudio.dll")]
        private static partial int Pa_Initialize();

        [LibraryImport("libportaudio.dll")]
        private static partial int Pa_Terminate();

        [LibraryImport("libportaudio.dll")]
        private static partial IntPtr Pa_GetVersionInfo();

        [LibraryImport("libportaudio.dll")]
        private static partial IntPtr Pa_GetErrorText(int code);

        [LibraryImport("libportaudio.dll")]
        private static partial int Pa_GetDefaultOutputDevice();

        [LibraryImport("libportaudio.dll")]
        private static partial IntPtr Pa_GetDeviceInfo(int device);

        [LibraryImport("libportaudio.dll")]
        private static partial int Pa_GetDeviceCount();

        [LibraryImport("libportaudio.dll")]
        private static partial int Pa_OpenStream(
            IntPtr stream,
            IntPtr inputParameters,
            IntPtr outputParameters,
            double sampleRate,
            long framesPerBuffer,
            PaStreamFlags streamFlags,
            PaStreamCallback? streamCallback,
            IntPtr userData);

        [LibraryImport("libportaudio.dll")]
        private static partial int Pa_StartStream(IntPtr stream);

        [LibraryImport("libportaudio.dll")]
        private static partial int Pa_WriteStream(IntPtr stream, IntPtr buffer, long frames);

        [LibraryImport("libportaudio.dll")]
        private static partial int Pa_AbortStream(IntPtr stream);

        [LibraryImport("libportaudio.dll")]
        private static partial int Pa_CloseStream(IntPtr stream);

        public int Initialize() => Pa_Initialize();

        public int Terminate() => Pa_Terminate();

        public IntPtr GetVersionInfo() => Pa_GetVersionInfo();

        public IntPtr GetErrorText(int code) => Pa_GetErrorText(code);

        public int GetDefaultOutputDevice() => Pa_GetDefaultOutputDevice();

        public IntPtr GetDeviceInfo(int device) => Pa_GetDeviceInfo(device);

        public int GetDeviceCount() => Pa_GetDeviceCount();

        public int OpenStream(IntPtr stream, IntPtr inputParameters, IntPtr outputParameters, double sampleRate,
            long framesPerBuffer,
            PaStreamFlags streamFlags, PaStreamCallback? streamCallback, IntPtr userData)
            => Pa_OpenStream(stream, inputParameters, outputParameters, sampleRate, framesPerBuffer, streamFlags, streamCallback, userData);

        public int StartStream(IntPtr stream) => Pa_StartStream(stream);

        public int WriteStream(IntPtr stream, IntPtr buffer, long frames) => Pa_WriteStream(stream, buffer, frames);

        public int AbortStream(IntPtr stream) => Pa_AbortStream(stream);

        public int CloseStream(IntPtr stream) => Pa_CloseStream(stream);
    }

    [SupportedOSPlatform("Linux")]
    private partial class Linux : INativeBindings {
        [LibraryImport("libportaudio.so.2")]
        private static partial int Pa_Initialize();

        [LibraryImport("libportaudio.so.2")]
        private static partial int Pa_Terminate();

        [LibraryImport("libportaudio.so.2")]
        private static partial IntPtr Pa_GetVersionInfo();

        [LibraryImport("libportaudio.so.2")]
        private static partial IntPtr Pa_GetErrorText(int code);

        [LibraryImport("libportaudio.so.2")]
        private static partial int Pa_GetDefaultOutputDevice();

        [LibraryImport("libportaudio.so.2")]
        private static partial IntPtr Pa_GetDeviceInfo(int device);

        [LibraryImport("libportaudio.so.2")]
        private static partial int Pa_GetDeviceCount();

        [LibraryImport("libportaudio.so.2")]
        private static partial int Pa_OpenStream(
            IntPtr stream,
            IntPtr inputParameters,
            IntPtr outputParameters,
            double sampleRate,
            long framesPerBuffer,
            PaStreamFlags streamFlags,
            PaStreamCallback? streamCallback,
            IntPtr userData);

        [LibraryImport("libportaudio.so.2")]
        private static partial int Pa_StartStream(IntPtr stream);

        [LibraryImport("libportaudio.so.2")]
        private static partial int Pa_WriteStream(IntPtr stream, IntPtr buffer, long frames);

        [LibraryImport("libportaudio.so.2")]
        private static partial int Pa_AbortStream(IntPtr stream);

        [LibraryImport("libportaudio.so.2")]
        private static partial int Pa_CloseStream(IntPtr stream);

        public int Initialize() => Pa_Initialize();

        public int Terminate() => Pa_Terminate();

        public IntPtr GetVersionInfo() => Pa_GetVersionInfo();

        public IntPtr GetErrorText(int code) => Pa_GetErrorText(code);

        public int GetDefaultOutputDevice() => Pa_GetDefaultOutputDevice();

        public IntPtr GetDeviceInfo(int device) => Pa_GetDeviceInfo(device);

        public int GetDeviceCount() => Pa_GetDeviceCount();

        public int OpenStream(IntPtr stream, IntPtr inputParameters, IntPtr outputParameters, double sampleRate,
            long framesPerBuffer,
            PaStreamFlags streamFlags, PaStreamCallback? streamCallback, IntPtr userData)
            => Pa_OpenStream(stream, inputParameters, outputParameters, sampleRate, framesPerBuffer, streamFlags, streamCallback, userData);

        public int StartStream(IntPtr stream) => Pa_StartStream(stream);

        public int WriteStream(IntPtr stream, IntPtr buffer, long frames) => Pa_WriteStream(stream, buffer, frames);

        public int AbortStream(IntPtr stream) => Pa_AbortStream(stream);

        public int CloseStream(IntPtr stream) => Pa_CloseStream(stream);
    }

    [SupportedOSPlatform("OSX")]
    private partial class MacOS : INativeBindings {
        [LibraryImport("libportaudio.2.dylib")]
        private static partial int Pa_Initialize();

        [LibraryImport("libportaudio.2.dylib")]
        private static partial int Pa_Terminate();

        [LibraryImport("libportaudio.2.dylib")]
        private static partial IntPtr Pa_GetVersionInfo();

        [LibraryImport("libportaudio.2.dylib")]
        private static partial IntPtr Pa_GetErrorText(int code);

        [LibraryImport("libportaudio.2.dylib")]
        private static partial int Pa_GetDefaultOutputDevice();

        [LibraryImport("libportaudio.2.dylib")]
        private static partial IntPtr Pa_GetDeviceInfo(int device);

        [LibraryImport("libportaudio.2.dylib")]
        private static partial int Pa_GetDeviceCount();

        [LibraryImport("libportaudio.2.dylib")]
        private static partial int Pa_OpenStream(
            IntPtr stream,
            IntPtr inputParameters,
            IntPtr outputParameters,
            double sampleRate,
            long framesPerBuffer,
            PaStreamFlags streamFlags,
            PaStreamCallback? streamCallback,
            IntPtr userData);

        [LibraryImport("libportaudio.2.dylib")]
        private static partial int Pa_StartStream(IntPtr stream);

        [LibraryImport("libportaudio.2.dylib")]
        private static partial int Pa_WriteStream(IntPtr stream, IntPtr buffer, long frames);

        [LibraryImport("libportaudio.2.dylib")]
        private static partial int Pa_AbortStream(IntPtr stream);

        [LibraryImport("libportaudio.2.dylib")]
        private static partial int Pa_CloseStream(IntPtr stream);

        public int Initialize() => Pa_Initialize();

        public int Terminate() => Pa_Terminate();

        public IntPtr GetVersionInfo() => Pa_GetVersionInfo();

        public IntPtr GetErrorText(int code) => Pa_GetErrorText(code);

        public int GetDefaultOutputDevice() => Pa_GetDefaultOutputDevice();

        public IntPtr GetDeviceInfo(int device) => Pa_GetDeviceInfo(device);

        public int GetDeviceCount() => Pa_GetDeviceCount();

        public int OpenStream(IntPtr stream, IntPtr inputParameters, IntPtr outputParameters, double sampleRate,
            long framesPerBuffer,
            PaStreamFlags streamFlags, PaStreamCallback? streamCallback, IntPtr userData)
            => Pa_OpenStream(stream, inputParameters, outputParameters, sampleRate, framesPerBuffer, streamFlags, streamCallback, userData);

        public int StartStream(IntPtr stream) => Pa_StartStream(stream);

        public int WriteStream(IntPtr stream, IntPtr buffer, long frames) => Pa_WriteStream(stream, buffer, frames);

        public int AbortStream(IntPtr stream) => Pa_AbortStream(stream);

        public int CloseStream(IntPtr stream) => Pa_CloseStream(stream);
    }
}
