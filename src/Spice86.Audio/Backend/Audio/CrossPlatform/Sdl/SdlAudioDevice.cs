namespace Spice86.Audio.Backend.Audio.CrossPlatform.Sdl;

using System;
using System.Runtime.InteropServices;
using System.Threading;

/// <summary>
/// SDL audio device abstraction. Manages the audio thread and callback lifecycle.
/// Reference: SDL_AudioDevice from SDL_sysaudio.h, open_audio_device/close_audio_device/SDL_RunAudio from SDL_audio.c
/// </summary>
internal sealed class SdlAudioDevice {
    private readonly ISdlAudioDriver _driver;
    private readonly object _mixerLock = new();
    private Thread? _audioThread;
    private volatile bool _shutdown;
    private volatile bool _paused = true;
    private volatile bool _enabled;
    private IntPtr _workBuffer;
    private SdlAudioDeviceCore? _core;

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
    /// Whether the device is enabled.
    /// Reference: SDL_AtomicGet(&amp;device-&gt;enabled)
    /// </summary>
    internal bool Enabled => _enabled;

    /// <summary>
    /// Marks the device as disconnected.
    /// Reference: SDL_OpenedAudioDeviceDisconnected
    /// </summary>
    internal void SetDeviceDisconnected() {
        _enabled = false;
    }

    /// <summary>
    /// Opens the device and creates the audio thread.
    /// Reference: open_audio_device() from SDL_audio.c
    /// The device starts paused. Call Start() to unpause.
    /// The audio thread is created here and waits for the startup semaphore,
    /// matching SDL's open_audio_device which creates the thread and SemWaits.
    /// </summary>
    public bool Open(AudioSpec desiredSpec) {
        // Reference: open_audio_device lines 1468-1470
        _shutdown = false;
        _paused = true;
        _enabled = true;
        LastError = null;

        int bufferFrames = desiredSpec.BufferFrames > 0
            ? desiredSpec.BufferFrames
            : GetDefaultSamplesFromFreq(desiredSpec.SampleRate);
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

        // Reference: open_audio_device line 1512
        // device->work_buffer = (Uint8 *)SDL_malloc(device->callbackspec.size)
        _workBuffer = Marshal.AllocHGlobal(BufferSizeBytes);
        unsafe {
            NativeMemory.Clear(_workBuffer.ToPointer(), (nuint)BufferSizeBytes);
        }

        // Reference: open_audio_device lines 1548-1572
        // SDL creates the audio thread during open_audio_device and waits
        // for it to signal via a semaphore that ThreadInit has completed.
        using (SemaphoreSlim startupSemaphore = new SemaphoreSlim(0, 1)) {
            _audioThread = new Thread(() => RunAudio(startupSemaphore)) {
                Name = "SDL-Audio-Playback",
                IsBackground = true
            };
            _audioThread.Start();
            startupSemaphore.Wait();
        }

        return true;
    }

    /// <summary>
    /// Unpauses the audio device.
    /// Reference: SDL_PauseAudioDevice(device, 0)
    /// </summary>
    public void Start() {
        _paused = false;
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
    /// Reference: close_audio_device() from SDL_audio.c lines 1196-1236
    /// </summary>
    public void Close() {
        // Reference: close_audio_device lines 1204-1209
        // Lock, set paused+shutdown+enabled, unlock, then wait for thread.
        lock (_mixerLock) {
            _paused = true;
            _shutdown = true;
            _enabled = false;
        }

        if (_audioThread != null && _audioThread.IsAlive) {
            _audioThread.Join(TimeSpan.FromSeconds(2));
        }
        _audioThread = null;
        _core = null;

        if (_workBuffer != IntPtr.Zero) {
            Marshal.FreeHGlobal(_workBuffer);
            _workBuffer = IntPtr.Zero;
        }

        _driver.CloseDevice(this);
    }

    /// <summary>
    /// The audio thread main loop.
    /// Reference: SDL_RunAudio from SDL_audio.c lines 672-804
    /// </summary>
    private unsafe void RunAudio(SemaphoreSlim startupSemaphore) {
        // SDL_SetThreadPriority(SDL_THREAD_PRIORITY_TIME_CRITICAL)
        Thread.CurrentThread.Priority = ThreadPriority.Highest;

        // SDL_SemPost(startup_data->startup_semaphore)
        startupSemaphore.Release();

        // current_audio.impl.ThreadInit(device)
        _driver.ThreadInit(this);

        // Loop, filling the audio buffers
        while (!_shutdown) {
            IntPtr data;

            // if (!device->stream && SDL_AtomicGet(&device->enabled))
            if (_enabled) {
                data = _driver.GetDeviceBuf(this);
            } else {
                data = IntPtr.Zero;
            }

            bool usingWorkBuffer = data == IntPtr.Zero;
            if (usingWorkBuffer) {
                data = _workBuffer;
            }

            int dataLen = BufferSizeBytes;

            // SDL_LockMutex(device->mixer_lock)
            lock (_mixerLock) {
                if (_paused) {
                    // SDL_memset(data, device->callbackspec.silence, data_len)
                    NativeMemory.Clear(data.ToPointer(), (nuint)dataLen);
                } else if (_core != null) {
                    _core.FillDeviceBuffer(data, dataLen);
                }
            }
            // SDL_UnlockMutex(device->mixer_lock)

            if (usingWorkBuffer) {
                // nothing to do; pause like we queued a buffer to play.
                // delay = ((device->spec.samples * 1000) / device->spec.freq)
                int delay = (SampleFrames * 1000) / ObtainedSpec.SampleRate;
                Thread.Sleep(delay);
            } else {
                // current_audio.impl.PlayDevice(device)
                _driver.PlayDevice(this);
                // current_audio.impl.WaitDevice(device)
                _driver.WaitDevice(this);
            }
        }

        // Wait for the audio to drain.
        // delay = ((device->spec.samples * 1000) / device->spec.freq) * 2
        int drainDelay = ((SampleFrames * 1000) / ObtainedSpec.SampleRate) * 2;
        if (drainDelay > 100) {
            drainDelay = 100;
        }
        Thread.Sleep(drainDelay);

        // current_audio.impl.ThreadDeinit(device)
        _driver.ThreadDeinit(this);
    }

    /// <summary>
    /// Fills the audio buffer via the callback or with silence.
    /// Reference: SDL_RunAudio callback invocation (lines 720-770)
    /// </summary>
    internal unsafe void FillAudioBuffer(IntPtr bufferPtr, int bufferBytes) {
        // Reference: SDL_RunAudio lines 740-743
        if (_paused) {
            NativeMemory.Clear(bufferPtr.ToPointer(), (nuint)bufferBytes);
            return;
        }

        if (_core != null) {
            _core.FillDeviceBuffer(bufferPtr, bufferBytes);
            return;
        }

        NativeMemory.Clear(bufferPtr.ToPointer(), (nuint)bufferBytes);
    }

    /// <summary>
    /// Computes the default sample frames from frequency.
    /// Reference: GetDefaultSamplesFromFreq in SDL_audio.c
    /// </summary>
    private static int GetDefaultSamplesFromFreq(int frequency) {
        // Pick a default of ~46 ms at desired frequency
        int maxSampleFrames = (frequency / 1000) * 46;
        int current = 1;
        while (current < maxSampleFrames) {
            current *= 2;
        }
        return current;
    }
}
