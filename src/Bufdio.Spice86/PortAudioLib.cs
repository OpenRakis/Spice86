namespace Bufdio.Spice86;

using Bufdio.Spice86.Bindings.PortAudio;
using Bufdio.Spice86.Bindings.PortAudio.Structs;
using Bufdio.Spice86.Exceptions;
using Bufdio.Spice86.Utilities;
using Bufdio.Spice86.Utilities.Extensions;

using System;
using System.Collections.Generic;

/// <summary>
/// Provides functionalities to retrieve, configure and manage current PortAudio environment
/// that affects the whole library configuration.
/// </summary>
public sealed class PortAudioLib : IDisposable {
    private bool _disposed;

    internal static class Constants {
        /// <summary>
        /// 32-bit floats
        /// </summary>
        public const int PaSampleFormat = 0x00000001;
    }

    private readonly LibraryLoader _libraryLoader;
    private readonly AudioDevice _defaultOutputDevice;
    private readonly List<AudioDevice> _outputDevices = new();

    /// <summary>
    /// Gets whether the PortAudio library is already initialized.
    /// </summary>
    public bool IsPortAudioInitialized { get; private set; }

    /// <summary>
    /// Gets default output device information that is used by the current system.
    /// </summary>
    /// <exception cref="BufdioException">Thrown if PortAudio is not initialized.</exception>
    public AudioDevice DefaultOutputDevice {
        get {
            Ensure.That<BufdioException>(IsPortAudioInitialized, "PortAudio is not initialized.");
            return _defaultOutputDevice;
        }
    }

    /// <summary>
    /// Gets list of available audio output devices in the current system.
    /// Will throws <see cref="BufdioException"/> if PortAudio is not initialized.
    /// </summary>
    public IReadOnlyCollection<AudioDevice> OutputDevices {
        get {
            Ensure.That<BufdioException>(IsPortAudioInitialized, "PortAudio is not initialized.");
            return _outputDevices;
        }
    }

    /// <summary>
    /// Initialize and register PortAudio functionalities by providing path to PortAudio native libary.
    /// Leave path parameter empty in order to use system-wide library.
    /// </summary>
    /// <param name="portAudioPath">
    /// Path to port audio native libary, eg: portaudio.dll, libportaudio.so, libportaudio.dylib.
    /// </param>
    /// <exception cref="BufdioException">Thrown when output device is not available.</exception>
    public PortAudioLib(string? portAudioPath = default) {
        portAudioPath = string.IsNullOrEmpty(portAudioPath) ? GetPortAudioLibName() : portAudioPath;

        _libraryLoader = new LibraryLoader(portAudioPath);

        NativeMethods.PortAudioInitialize();

        int deviceCount = NativeMethods.PortAudioGetDeviceCount();

        Ensure.That<BufdioException>(deviceCount > 0, "No output devices are available.");

        int defaultDevice = NativeMethods.PortAudioGetDefaultOutputDevice();

        _defaultOutputDevice = defaultDevice.PaGetPaDeviceInfo().PaToAudioDevice(defaultDevice);
        _outputDevices = new List<AudioDevice>();

        for (int i = 0; i < deviceCount; i++) {
            PaDeviceInfo deviceInfo = i.PaGetPaDeviceInfo();

            if (deviceInfo.maxOutputChannels > 0) {
                _outputDevices.Add(deviceInfo.PaToAudioDevice(i));
            }
        }
        IsPortAudioInitialized = true;
    }

    /// <summary>
    /// Returns the name of system-provided PortAudio library
    /// </summary>
    /// <returns>A string containing the filename of the system-provided PortAudio library</returns>
    public static string GetPortAudioLibName() => NativeMethods.GetPortAudioLibName();

    /// <inheritdoc/>
    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing) {
        if (_disposed) {
            if (disposing) {
                _libraryLoader.Dispose();
            }
            _disposed = true;
        }
    }
}
