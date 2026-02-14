namespace Spice86.Core.Backend.Audio.CrossPlatform.Sdl;

using System;
using System.Threading;

/// <summary>
/// SDL audio device abstraction. Manages the audio thread and callback lifecycle.
/// Reference: SDL_AudioDevice from SDL_sysaudio.h, SDL_RunAudio from SDL_audio.c
/// </summary>
internal sealed class SdlAudioDevice {
    private readonly ISdlAudioDriver _driver;
    private readonly object _lock = new();
    private Thread? _audioThread;
    private volatile bool _shutdown;
    private volatile bool _paused = true;
    private SdlAudioDeviceCore? _core;

    /// <summary>
    /// Creates a new SDL audio device with the given platform-specific driver.
    /// </summary>
    public SdlAudioDevice(ISdlAudioDriver driver) {
        _driver = driver;
    }

    /// <summary>
    /// The requested audio spec.
    /// </summary>
    public AudioSpec Spec { get; private set; } = new AudioSpec();

    /// <summary>
    /// The obtained audio spec from the hardware.
    /// </summary>
    public AudioSpec ObtainedSpec { get; private set; } = new AudioSpec();

    /// <summary>
    /// Number of sample frames per callback period.
    /// </summary>
    public int SampleFrames { get; private set; }

    /// <summary>
    /// Buffer size in bytes for one callback period.
    /// </summary>
    public int BufferSizeBytes { get; private set; }

    /// <summary>
    /// Last error message from open or playback.
    /// </summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Whether shutdown has been requested.
    /// Reference: SDL_AtomicGet(&amp;device-&gt;shutdown)
    /// </summary>
    internal bool ShutdownRequested => _shutdown;

    /// <summary>
    /// Opens the device with the desired audio spec.
    /// Reference: open_audio_device() from SDL_audio.c
    /// </summary>
    public bool Open(AudioSpec desiredSpec) {
        _shutdown = false;
        _paused = true;
        LastError = null;

        int bufferFrames = desiredSpec.BufferFrames > 0
            ? desiredSpec.BufferFrames
            : GetDefaultSampleFramesFromFrequency(desiredSpec.SampleRate);
        AudioSpec requestedSpec = new AudioSpec {
            SampleRate = desiredSpec.SampleRate,
            Channels = desiredSpec.Channels,
            BufferFrames = bufferFrames,
            Callback = desiredSpec.Callback,
            PostmixCallback = desiredSpec.PostmixCallback
        };

        bool ok = _driver.OpenDevice(this, requestedSpec, out AudioSpec obtainedSpec, out int sampleFrames, out string? error);
        if (!ok) {
            LastError = error;
            return false;
        }

        Spec = requestedSpec;
        ObtainedSpec = obtainedSpec;
        SampleFrames = sampleFrames;
        BufferSizeBytes = SampleFrames * ObtainedSpec.Channels * sizeof(float);
        if (ObtainedSpec.Callback != null) {
            _core = new SdlAudioDeviceCore(ObtainedSpec, BufferSizeBytes);
        }
        return true;
    }

    /// <summary>
    /// Starts the audio thread.
    /// Reference: SDL_PauseAudioDevice(device, 0) + thread creation in open_audio_device()
    /// </summary>
    public void Start() {
        if (_audioThread == null) {
            _paused = false;
            _audioThread = new Thread(AudioThreadLoop) {
                Name = "SDL-Audio-Playback",
                IsBackground = true
            };
            _audioThread.Start();
        } else {
            _paused = false;
        }
    }

    /// <summary>
    /// Pauses audio playback.
    /// Reference: SDL_PauseAudioDevice(device, 1)
    /// </summary>
    public void Pause() {
        _paused = true;
    }

    /// <summary>
    /// Closes the device and stops the audio thread.
    /// Reference: close_audio_device() from SDL_audio.c
    /// </summary>
    public void Close() {
        _shutdown = true;
        if (_audioThread != null && _audioThread.IsAlive) {
            _audioThread.Join(TimeSpan.FromSeconds(2));
        }
        _audioThread = null;
        _paused = true;
        _core = null;
        _driver.CloseDevice(this);
    }

    /// <summary>
    /// The audio thread main loop.
    /// Reference: SDL_RunAudio from SDL_audio.c lines 669-800
    /// SDL flow: ThreadInit -> loop { GetDeviceBuf -> Lock -> Callback -> Unlock -> PlayDevice -> WaitDevice } -> ThreadDeinit
    /// </summary>
    private void AudioThreadLoop() {
        _driver.ThreadInit(this);

        // Reference: SDL_audio.c SDL_RunAudio lines 703-791
        // SDL flow: GetDeviceBuf -> Lock -> Callback -> Unlock -> PlayDevice -> WaitDevice
        // SdlPlaybackThread.Iterate handles GetDeviceBuf/Lock/Callback/Unlock/PlayDevice
        // WaitDevice follows immediately after, matching SDL exactly.
        while (!_shutdown) {
            if (!SdlPlaybackThread.Iterate(this, _driver, _lock)) {
                break;
            }

            // Reference: SDL_RunAudio line 789: current_audio.impl.WaitDevice(device)
            // WaitDevice blocks until the hardware buffer has drained enough
            // for the next iteration to succeed.
            if (!_driver.WaitDevice(this)) {
                break;
            }
        }

        SdlPlaybackThread.Shutdown(this, _driver);
    }

    /// <summary>
    /// Fills the audio buffer via the callback or with silence.
    /// Reference: SDL_RunAudio callback invocation (lines 720-770)
    /// </summary>
    internal unsafe void FillAudioBuffer(IntPtr bufferPtr, int bufferBytes) {
        if (_paused) {
            int pausedSampleCount = bufferBytes / sizeof(float);
            Span<float> pausedBuffer = new Span<float>(bufferPtr.ToPointer(), pausedSampleCount);
            pausedBuffer.Clear();
            return;
        }

        if (_core != null) {
            _core.FillDeviceBuffer(bufferPtr, bufferBytes);
            return;
        }

        int fallbackSampleCount = bufferBytes / sizeof(float);
        Span<float> fallbackBuffer = new Span<float>(bufferPtr.ToPointer(), fallbackSampleCount);
        fallbackBuffer.Clear();
    }

    /// <summary>
    /// Computes the default sample frames from frequency.
    /// Reference: GetDefaultSamplesFromFreq in SDL_audio.c
    /// </summary>
    private static int GetDefaultSampleFramesFromFrequency(int frequency) {
        int maxSampleFrames = (frequency / 1000) * 46;
        int current = 1;
        while (current < maxSampleFrames) {
            current *= 2;
        }
        return current;
    }
}
