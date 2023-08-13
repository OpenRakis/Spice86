namespace Spice86.Core.Backend.Audio.PortAudio;

using Bufdio.Spice86;
using Bufdio.Spice86.Exceptions;

using Serilog.Events;

using Spice86.Shared.Interfaces;

/// <summary>
/// Factory for instances of PortAudioPlayer.
/// Ensures the native library is loaded properly.
/// </summary>
public class PortAudioPlayerFactory {
    private static readonly object _lock = new();
    private static bool _loadedNativeLib;
    private readonly ILoggerService _loggerService;

    public PortAudioPlayerFactory(ILoggerService loggerService) {
        _loggerService = loggerService;
    }

    private void LoadNativeLibIfNeeded() {
        lock(_lock) {
            if(_loadedNativeLib) {
                return;
            }
            try {
                if (OperatingSystem.IsWindows()) {
                    const string path = "libportaudio.dll";
                    _loadedNativeLib = BufdioLib.InitializePortAudio(path);
                } else {
                    //rely on system-provided libportaudio.
                    _loadedNativeLib = BufdioLib.InitializePortAudio();
                }
            } catch (BufdioException e) {
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error(e, "The native PortAudio library could not be loaded");
                }
            }
        }
    }
    
    /// <summary>
    /// Creates an instance of PortAudioPlayer eventually loading the libportaudio native library if needed.
    /// </summary>
    /// <param name="sampleRate"></param>
    /// <param name="framesPerBuffer"></param>
    /// <param name="suggestedLatency"></param>
    /// <returns></returns>
    public PortAudioPlayer? Create(int sampleRate, int framesPerBuffer, double? suggestedLatency = null) {
        if (!_loadedNativeLib && !BufdioLib.IsPortAudioInitialized) {
            LoadNativeLibIfNeeded();
        }
        if(!_loadedNativeLib) {
            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                string libName = BufdioLib.GetPortAudioLibName();
                _loggerService.Error(
                    "{libName} native library could not be loaded. If you are not not on windows ensure it is installed on your system.",
                    libName);
            }
            return null;
        }
        return new PortAudioPlayer(framesPerBuffer, new AudioFormat(SampleRate: sampleRate, Channels: 2,
            SampleFormat: SampleFormat.IeeeFloat32), suggestedLatency);
    }
}