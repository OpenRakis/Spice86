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
    private readonly ILoggerService _loggerService;
    private const string LibPortAudioDllWin64 = "libportaudio.dll";

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="loggerService">The logger service implementation.</param>
    public PortAudioPlayerFactory(ILoggerService loggerService) => _loggerService = loggerService;

    internal bool CanPortAudioBeLoaded =>
        (OperatingSystem.IsWindows() && Environment.Is64BitOperatingSystem && File.Exists(LibPortAudioDllWin64)) ||
        OperatingSystem.IsLinux() ||
        OperatingSystem.IsMacOS();


    private PortAudioLib LoadPortAudioLibrary() {
        if (OperatingSystem.IsWindows() && Environment.Is64BitOperatingSystem) {
            if (File.Exists(LibPortAudioDllWin64)) {
                return new PortAudioLib(LibPortAudioDllWin64);
            }
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) {
            //rely on system-provided libportaudio.
            return new PortAudioLib();
        }

        throw new PlatformNotSupportedException();
    }

    /// <summary>
    /// Creates an instance of PortAudioPlayer eventually loading the libportaudio native library if needed.
    /// </summary>
    /// <param name="sampleRate">The backend audio samplerate</param>
    /// <param name="framesPerBuffer">The number of audio frames per buffer</param>
    /// <param name="suggestedLatency">The latency to suggest to PortAudio</param>
    /// <returns>An instance of the PortAudioPlayer, or <c>null</c> if the native library failed to load or was not found.</returns>
    public PortAudioPlayer? Create(int sampleRate, int framesPerBuffer, double? suggestedLatency = null) {
        lock(_lock) {
            try {
                return new PortAudioPlayer(LoadPortAudioLibrary(), framesPerBuffer,
                    new AudioFormat(SampleRate: sampleRate, Channels: 2, SampleFormat.IeeeFloat32), suggestedLatency);
            } catch (DllNotFoundException e) {
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error(e, "The native PortAudio library could not be loaded");
                }
            } catch (BufdioException e) {
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error(e, "No audio output device could be found");
                }
            } catch (PlatformNotSupportedException e) {
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error(e, "A compatible PortAudio library could not be found for the current platform");
                }
            }
        }
        return null;
    }
}