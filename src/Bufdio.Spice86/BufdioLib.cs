using System;
using System.Collections.Generic;

using Bufdio.Spice86.Bindings.PortAudio;
using Bufdio.Spice86.Exceptions;
using Bufdio.Spice86.Utilities;
using Bufdio.Spice86.Utilities.Extensions;

namespace Bufdio.Spice86;

/// <summary>
/// Provides functionalities to retrieve, configure and manage current Bufdio environment
/// that affects the whole library configuration.
/// </summary>
public static class BufdioLib {
    internal static class Constants {
        public const PaBinding.PaSampleFormat PaSampleFormat = PaBinding.PaSampleFormat.paFloat32;
    }

    private static AudioDevice _defaultOutputDevice;
    private static List<AudioDevice> _outputDevices = new();

    /// <summary>
    /// Gets whether or not the PortAudio library is already initialized.
    /// </summary>
    /// <exception cref="BufdioException">Thrown if PortAudio is not initialized.</exception>
    public static bool IsPortAudioInitialized { get; private set; }

    /// <summary>
    /// Gets default output device information that is used by the current system.
    /// </summary>
    /// <exception cref="BufdioException">Thrown if PortAudio is not initialized.</exception>
    public static AudioDevice DefaultOutputDevice {
        get {
            Ensure.That<BufdioException>(IsPortAudioInitialized, "PortAudio is not initialized.");
            return _defaultOutputDevice;
        }
    }

    /// <summary>
    /// Gets list of available audio output devices in the current system.
    /// Will throws <see cref="BufdioException"/> if PortAudio is not initialized.
    /// </summary>
    public static IReadOnlyCollection<AudioDevice> OutputDevices {
        get {
            Ensure.That<BufdioException>(IsPortAudioInitialized, "PortAudio is not initialized.");
            return _outputDevices;
        }
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
    public static bool InitializePortAudio(string? portAudioPath = default) {
        if (IsPortAudioInitialized) {
            return false;
        }

        portAudioPath = string.IsNullOrEmpty(portAudioPath) ? GetPortAudioLibName() : portAudioPath;

        var loader = new LibraryLoader();
        var loadedNativeLib = loader.Initialize(portAudioPath);
        if (!loadedNativeLib) {
            return false;
        }
        PaBinding.InitializeBindings(loader);
        PaBinding.Pa_Initialize();

        int deviceCount = PaBinding.Pa_GetDeviceCount();
        Ensure.That<BufdioException>(deviceCount > 0, "No output devices are available.");

        int defaultDevice = PaBinding.Pa_GetDefaultOutputDevice();
        _defaultOutputDevice = defaultDevice.PaGetPaDeviceInfo().PaToAudioDevice(defaultDevice);
        _outputDevices = new List<AudioDevice>();

        for (int i = 0; i < deviceCount; i++) {
            PaBinding.PaDeviceInfo deviceInfo = i.PaGetPaDeviceInfo();

            if (deviceInfo.maxOutputChannels > 0) {
                _outputDevices.Add(deviceInfo.PaToAudioDevice(i));
            }
        }

        IsPortAudioInitialized = true;
        return true;
    }

    private static string GetPortAudioLibName() {
        if (PlatformInfo.IsWindows) {
            return "libportaudio-2.dll";
        } else if (PlatformInfo.IsLinux) {
            return "libportaudio.so.2";
        } else if (PlatformInfo.IsOSX) {
            return "libportaudio.2.dylib";
        } else {
            throw new NotImplementedException();
        }
    }
}
