namespace Spice86.Core.Backend.Audio.CrossPlatform.Sdl.Windows;

using System;
using System.Threading;

internal interface ISdlAudioDriver {
    bool OpenDevice(SdlAudioDevice device, AudioSpec desiredSpec, out AudioSpec obtainedSpec, out int sampleFrames, out string? error);
    void CloseDevice(SdlAudioDevice device);
    bool WaitDevice(SdlAudioDevice device);
    IntPtr GetDeviceBuffer(SdlAudioDevice device, out int bufferBytes);
    bool PlayDevice(SdlAudioDevice device, IntPtr buffer, int bufferBytes);
    void ThreadInit(SdlAudioDevice device);
    void ThreadDeinit(SdlAudioDevice device);
}

internal sealed class SdlAudioDevice {
    private readonly ISdlAudioDriver _driver;
    private readonly object _lock = new object();
    private Thread? _audioThread;
    private volatile bool _shutdown;
    private volatile bool _paused = true;
    private SdlAudioDeviceCore? _core;

    public SdlAudioDevice(ISdlAudioDriver driver) {
        _driver = driver;
    }

    public AudioSpec Spec { get; private set; } = new AudioSpec();
    public AudioSpec ObtainedSpec { get; private set; } = new AudioSpec();
    public int SampleFrames { get; private set; }
    public int BufferSizeBytes { get; private set; }
    public string? LastError { get; private set; }
    internal bool ShutdownRequested => _shutdown;

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

    public void Start() {
        if (_audioThread == null) {
            _audioThread = new Thread(AudioThreadLoop) {
                Name = "SDL-Audio-Playback",
                IsBackground = true
            };
            _audioThread.Start();
        }

        _paused = false;
    }

    public void Pause() {
        _paused = true;
    }

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

    private void AudioThreadLoop() {
        _driver.ThreadInit(this);

        while (!_shutdown) {
            if (!SdlPlaybackThread.Iterate(this, _driver, _lock)) {
                break;
            }

            if (!_driver.WaitDevice(this)) {
                break;
            }
        }

        SdlPlaybackThread.Shutdown(this, _driver);
    }

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

    private static int GetDefaultSampleFramesFromFrequency(int frequency) {
        int maxSampleFrames = (frequency / 1000) * 46;
        int current = 1;
        while (current < maxSampleFrames) {
            current *= 2;
        }
        return current;
    }
}
