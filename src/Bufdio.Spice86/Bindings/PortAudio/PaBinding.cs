using System;
using System.Runtime.InteropServices;

namespace Bufdio.Spice86.Bindings.PortAudio;

internal static partial class PaBinding {
    public static partial class Windows {
        [DllImport("libportaudio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Pa_Initialize();

        [DllImport("libportaudio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Pa_Terminate();

        [DllImport("libportaudio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr Pa_GetVersionInfo();

        [DllImport("libportaudio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr Pa_GetErrorText(int code);

        [DllImport("libportaudio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Pa_GetDefaultOutputDevice();

        [DllImport("libportaudio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr Pa_GetDeviceInfo(int device);

        [DllImport("libportaudio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Pa_GetDeviceCount();

        [DllImport("libportaudio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Pa_OpenStream(
            IntPtr stream,
            IntPtr inputParameters,
            IntPtr outputParameters,
            double sampleRate,
            long framesPerBuffer,
            PaStreamFlags streamFlags,
            PaStreamCallback? streamCallback,
            IntPtr userData);

        [DllImport("libportaudio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Pa_StartStream(IntPtr stream);

        [DllImport("libportaudio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Pa_WriteStream(IntPtr stream, IntPtr buffer, long frames);

        [DllImport("libportaudio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Pa_AbortStream(IntPtr stream);

        [DllImport("libportaudio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Pa_CloseStream(IntPtr stream);
    }
    
    public static partial class Linux {
        [DllImport("libportaudio.so.2", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Pa_Initialize();

        [DllImport("libportaudio.so.2", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Pa_Terminate();

        [DllImport("libportaudio.so.2", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr Pa_GetVersionInfo();

        [DllImport("libportaudio.so.2", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr Pa_GetErrorText(int code);

        [DllImport("libportaudio.so.2", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Pa_GetDefaultOutputDevice();

        [DllImport("libportaudio.so.2", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr Pa_GetDeviceInfo(int device);

        [DllImport("libportaudio.so.2", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Pa_GetDeviceCount();

        [DllImport("libportaudio.so.2", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Pa_OpenStream(
            IntPtr stream,
            IntPtr inputParameters,
            IntPtr outputParameters,
            double sampleRate,
            long framesPerBuffer,
            PaStreamFlags streamFlags,
            PaStreamCallback? streamCallback,
            IntPtr userData);

        [DllImport("libportaudio.so.2", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Pa_StartStream(IntPtr stream);

        [DllImport("libportaudio.so.2", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Pa_WriteStream(IntPtr stream, IntPtr buffer, long frames);

        [DllImport("libportaudio.so.2", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Pa_AbortStream(IntPtr stream);

        [DllImport("libportaudio.so.2", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Pa_CloseStream(IntPtr stream);
    }
    
    public static partial class OSX {
        [DllImport("libportaudio.2.dylib", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Pa_Initialize();

        [DllImport("libportaudio.2.dylib", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Pa_Terminate();

        [DllImport("libportaudio.2.dylib", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr Pa_GetVersionInfo();

        [DllImport("libportaudio.2.dylib", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr Pa_GetErrorText(int code);

        [DllImport("libportaudio.2.dylib", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Pa_GetDefaultOutputDevice();

        [DllImport("libportaudio.2.dylib", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr Pa_GetDeviceInfo(int device);

        [DllImport("libportaudio.2.dylib", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Pa_GetDeviceCount();

        [DllImport("libportaudio.2.dylib", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Pa_OpenStream(
            IntPtr stream,
            IntPtr inputParameters,
            IntPtr outputParameters,
            double sampleRate,
            long framesPerBuffer,
            PaStreamFlags streamFlags,
            PaStreamCallback? streamCallback,
            IntPtr userData);

        [DllImport("libportaudio.2.dylib", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Pa_StartStream(IntPtr stream);

        [DllImport("libportaudio.2.dylib", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Pa_WriteStream(IntPtr stream, IntPtr buffer, long frames);

        [DllImport("libportaudio.2.dylib", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Pa_AbortStream(IntPtr stream);

        [DllImport("libportaudio.2.dylib", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Pa_CloseStream(IntPtr stream);
    }
}
