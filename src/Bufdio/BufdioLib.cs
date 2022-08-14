using System;
using System.Collections.Generic;
using Bufdio.Bindings.PortAudio;
using Bufdio.Exceptions;
using Bufdio.Utilities;
using Bufdio.Utilities.Extensions;
using FFmpeg.AutoGen;

namespace Bufdio;

/// <summary>
/// Provides functionalities to retrieve, configure and manage current Bufdio environment
/// that affects the whole library configuration.
/// </summary>
public static class BufdioLib
{
    internal static class Constants
    {
        public const AVSampleFormat FFmpegSampleFormat = AVSampleFormat.AV_SAMPLE_FMT_FLT;
        public const PaBinding.PaSampleFormat PaSampleFormat = PaBinding.PaSampleFormat.paFloat32;
    }

    private static AudioDevice _defaultOutputDevice;
    private static List<AudioDevice> _outputDevices;

    /// <summary>
    /// Gets whether or not the FFmpeg is already initialized.
    /// </summary>
    public static bool IsFFmpegInitialized { get; private set; }

    /// <summary>
    /// Gets whether or not the PortAudio library is already initialized.
    /// </summary>
    /// <exception cref="BufdioException">Thrown if PortAudio is not initialized.</exception>
    public static bool IsPortAudioInitialized { get; private set; }

    /// <summary>
    /// Gets default output device information that is used by the current system.
    /// </summary>
    /// <exception cref="BufdioException">Thrown if PortAudio is not initialized.</exception>
    public static AudioDevice DefaultOutputDevice
    {
        get
        {
            Ensure.That<BufdioException>(IsPortAudioInitialized, "PortAudio is not initialized.");
            return _defaultOutputDevice;
        }
    }

    /// <summary>
    /// Gets list of available audio output devices in the current system.
    /// Will throws <see cref="BufdioException"/> if PortAudio is not initialized.
    /// </summary>
    public static IReadOnlyCollection<AudioDevice> OutputDevices
    {
        get
        {
            Ensure.That<BufdioException>(IsPortAudioInitialized, "PortAudio is not initialized.");
            return _outputDevices;
        }
    }

    /// <summary>
    /// Initialize and register FFmpeg functionalities by providing path to FFmpeg native libraries.
    /// Leave directory parameter empty in order to use system-wide libraries.
    /// Will returns immediately if already initialized.
    /// </summary>
    /// <param name="ffmpegDirectory">
    /// Path to FFmpeg native libaries, leave this empty to use system-wide libraries.
    /// </param>
    public static void InitializeFFmpeg(string ffmpegDirectory = default)
    {
        if (IsFFmpegInitialized)
        {
            return;
        }

        ffmpeg.RootPath = ffmpegDirectory ?? "";
        ffmpeg.av_log_set_level(ffmpeg.AV_LOG_QUIET);
        IsFFmpegInitialized = true;
    }

    /// <summary>
    /// Initialize and register PortAudio functionalities by providing path to PortAudio native libary.
    /// Leave path parameter empty in order to use system-wide library.
    /// Will returns immediately if already initialized.
    /// </summary>
    /// <param name="portAudioPath">
    /// Path to port audio native libary, eg: portaudio.dll, libportaudio.so, libportaudio.dylib.
    /// </param>
    /// <exception cref="BufdioException">Thrown when output device is not available.</exception>
    public static void InitializePortAudio(string portAudioPath = default)
    {
        if (IsPortAudioInitialized)
        {
            return;
        }

        portAudioPath = string.IsNullOrEmpty(portAudioPath) ? GetPortAudioLibName() : portAudioPath;

        PaBinding.InitializeBindings(new LibraryLoader(portAudioPath));
        PaBinding.Pa_Initialize();

        var deviceCount = PaBinding.Pa_GetDeviceCount();
        Ensure.That<BufdioException>(deviceCount > 0, "No output devices are available.");

        var defaultDevice = PaBinding.Pa_GetDefaultOutputDevice();
        _defaultOutputDevice = defaultDevice.PaGetPaDeviceInfo().PaToAudioDevice(defaultDevice);
        _outputDevices = new List<AudioDevice>();

        for (var i = 0; i < deviceCount; i++)
        {
            var deviceInfo = i.PaGetPaDeviceInfo();

            if (deviceInfo.maxOutputChannels > 0)
            {
                _outputDevices.Add(deviceInfo.PaToAudioDevice(i));
            }
        }

        IsPortAudioInitialized = true;
    }

    private static string GetPortAudioLibName()
    {
        if (PlatformInfo.IsWindows)
        {
            return "libportaudio-2.dll";
        }
        else if (PlatformInfo.IsLinux)
        {
            return "libportaudio.so.2";
        }
        else if (PlatformInfo.IsOSX)
        {
            return "libportaudio.2.dylib";
        }
        else
        {
            throw new NotImplementedException();
        }
    }
}
