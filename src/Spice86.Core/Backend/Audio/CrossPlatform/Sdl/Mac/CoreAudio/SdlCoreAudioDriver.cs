namespace Spice86.Core.Backend.Audio.CrossPlatform.Sdl.Mac.CoreAudio;

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

/// <summary>
/// CoreAudio (AudioQueue) driver implementing ISdlAudioDriver.
/// This is an exact port of SDL_coreaudio.m (SDL2) to C#.
/// </summary>
[SupportedOSPlatform("osx")]
internal sealed class SdlCoreAudioDriver : ISdlAudioDriver {
    private IntPtr _audioQueue;
    private IntPtr[] _audioBuffers = Array.Empty<IntPtr>();
    private IntPtr _mixBuffer;
    private int _mixBufferSize;
    private int _mixBufferOffset;
    private Thread? _audioQueueThread;
    private volatile bool _shutdown;
    private readonly ManualResetEventSlim _readySemaphore = new(false);
    private string? _threadError;
    private SdlAudioDevice? _device;
    private readonly object _mixerLock = new();

    // Must prevent GC of the delegate while AudioQueue is using it
    private CoreAudioNativeMethods.AudioQueueOutputCallback? _outputCallbackDelegate;
    private GCHandle _callbackHandle;
    private IntPtr _defaultRunLoopMode;

    /// <summary>
    /// Opens the CoreAudio AudioQueue device.
    /// Reference: COREAUDIO_OpenDevice (SDL_coreaudio.m line 1062)
    /// 
    /// Flow:
    /// 1. Setup AudioStreamBasicDescription for float PCM
    /// 2. Create audioqueue_thread which calls prepare_audioqueue
    /// 3. Wait for ready semaphore
    /// 4. Return success/failure
    /// </summary>
    public bool OpenDevice(SdlAudioDevice device, AudioSpec desiredSpec, out AudioSpec obtainedSpec, out int sampleFrames, out string? error) {
        obtainedSpec = desiredSpec;
        sampleFrames = 0;
        error = null;
        _device = device;
        _shutdown = false;
        _threadError = null;

        // Reference: COREAUDIO_OpenDevice line 1098-1126
        // Setup AudioStreamBasicDescription for float LE format
        // SDL always uses AUDIO_F32LSB for the device spec on macOS

        int channels = desiredSpec.Channels;
        int freq = desiredSpec.SampleRate;
        int bufferFrames = desiredSpec.BufferFrames;

        // Calculate buffer size: frames * channels * sizeof(float)
        // Reference: SDL_CalculateAudioSpec equivalent
        _mixBufferSize = bufferFrames * channels * sizeof(float);
        _mixBufferOffset = _mixBufferSize; // Start fully consumed (triggers first callback fill)

        // Allocate the mix buffer
        // Reference: prepare_audioqueue line ~949-953
        _mixBuffer = Marshal.AllocHGlobal(_mixBufferSize);
        unsafe {
            new Span<byte>(_mixBuffer.ToPointer(), _mixBufferSize).Clear();
        }

        // Get kCFRunLoopDefaultMode
        _defaultRunLoopMode = CoreAudioNativeMethods.GetDefaultRunLoopMode();

        // Reference: COREAUDIO_OpenDevice line 1127-1134
        // "This has to init in a new thread so it can get its own CFRunLoop."
        _readySemaphore.Reset();

        _audioQueueThread = new Thread(() => AudioQueueThreadProc(freq, channels, bufferFrames)) {
            Name = "CoreAudio-AudioQueue",
            IsBackground = true
        };
        _audioQueueThread.Start();

        // Reference: COREAUDIO_OpenDevice line 1137-1138
        // SDL_SemWait(this->hidden->ready_semaphore)
        _readySemaphore.Wait();

        if (_threadError != null) {
            error = _threadError;
            return false;
        }

        // Reference: COREAUDIO_OpenDevice line 1140-1143
        obtainedSpec = new AudioSpec {
            SampleRate = freq,
            Channels = channels,
            BufferFrames = bufferFrames,
            Callback = desiredSpec.Callback,
            PostmixCallback = desiredSpec.PostmixCallback
        };
        sampleFrames = bufferFrames;

        return true;
    }

    /// <summary>
    /// Closes the CoreAudio device.
    /// Reference: COREAUDIO_CloseDevice (SDL_coreaudio.m line 665)
    /// 
    /// Flow:
    /// 1. Set paused flag to feed silence from callback
    /// 2. AudioQueueFlush -> AudioQueueStop -> AudioQueueDispose
    /// 3. Wait for audioqueue_thread to finish
    /// 4. Free mix buffer
    /// </summary>
    public void CloseDevice(SdlAudioDevice device) {
        // Reference: COREAUDIO_CloseDevice line 679
        // "if callback fires again, feed silence; don't call into the app."
        // The shutdown flag is already set by SdlAudioDevice.Close()

        // Reference: COREAUDIO_CloseDevice line 681-683
        // "dispose of the audio queue before waiting on the thread, 
        //  or it might stall for a long time!"
        if (_audioQueue != IntPtr.Zero) {
            CoreAudioNativeMethods.AudioQueueFlush(_audioQueue);
            CoreAudioNativeMethods.AudioQueueStop(_audioQueue, 0);
            CoreAudioNativeMethods.AudioQueueDispose(_audioQueue, 0);
            _audioQueue = IntPtr.Zero;
        }

        // Reference: COREAUDIO_CloseDevice line 685-687
        // "SDL_WaitThread(this->hidden->thread, NULL)"
        _shutdown = true;
        if (_audioQueueThread != null && _audioQueueThread.IsAlive) {
            _audioQueueThread.Join(TimeSpan.FromSeconds(5));
        }
        _audioQueueThread = null;

        // Free the mix buffer
        if (_mixBuffer != IntPtr.Zero) {
            Marshal.FreeHGlobal(_mixBuffer);
            _mixBuffer = IntPtr.Zero;
        }

        // Free callback handle
        if (_callbackHandle.IsAllocated) {
            _callbackHandle.Free();
        }

        _audioBuffers = Array.Empty<IntPtr>();
    }

    /// <summary>
    /// WaitDevice for CoreAudio is a no-op.
    /// CoreAudio uses ProvidesOwnCallbackThread, so SDL_RunAudio's
    /// WaitDevice/GetDeviceBuffer/PlayDevice loop is never used.
    /// The SdlAudioDevice thread will just idle, and CloseDevice's
    /// shutdown flag will break it out.
    /// </summary>
    public bool WaitDevice(SdlAudioDevice device) {
        // Block until shutdown since CoreAudio manages its own callbacks
        // via CFRunLoop in the audioqueue_thread.
        // We sleep briefly to avoid busy-waiting.
        Thread.Sleep(100);
        return !device.ShutdownRequested;
    }

    /// <summary>
    /// GetDeviceBuffer for CoreAudio returns null.
    /// CoreAudio fills buffers directly in its outputCallback.
    /// </summary>
    public IntPtr GetDeviceBuffer(SdlAudioDevice device, out int bufferBytes) {
        bufferBytes = 0;
        return IntPtr.Zero;
    }

    /// <summary>
    /// PlayDevice for CoreAudio is a no-op.
    /// CoreAudio enqueues buffers directly in its outputCallback.
    /// </summary>
    public bool PlayDevice(SdlAudioDevice device, IntPtr buffer, int bufferBytes) {
        return true;
    }

    /// <summary>
    /// ThreadInit for CoreAudio - sets thread priority.
    /// Reference: audioqueue_thread line 1020
    /// SDL_SetThreadPriority(SDL_THREAD_PRIORITY_HIGH)
    /// </summary>
    public void ThreadInit(SdlAudioDevice device) {
        // CoreAudio manages its own thread. The SdlAudioDevice thread
        // is essentially idle for CoreAudio.
    }

    /// <summary>
    /// ThreadDeinit for CoreAudio is a no-op.
    /// </summary>
    public void ThreadDeinit(SdlAudioDevice device) {
    }

    /// <summary>
    /// The AudioQueue thread function.
    /// Reference: audioqueue_thread (SDL_coreaudio.m line 991)
    /// 
    /// Flow:
    /// 1. Call prepare_audioqueue (creates AudioQueue on this thread's CFRunLoop)
    /// 2. Signal ready semaphore
    /// 3. Loop CFRunLoopRunInMode until shutdown
    /// 4. On exit, drain remaining playback
    /// </summary>
    private void AudioQueueThreadProc(int sampleRate, int channels, int bufferFrames) {
        // Reference: audioqueue_thread line 1010-1013
        // prepare_audioqueue creates the AudioQueue bound to this thread's CFRunLoop
        if (!PrepareAudioQueue(sampleRate, channels, bufferFrames)) {
            _threadError = _threadError ?? "Failed to prepare AudioQueue";
            _readySemaphore.Set();
            return;
        }

        // Reference: audioqueue_thread line 1020
        // SDL_SetThreadPriority(SDL_THREAD_PRIORITY_HIGH)
        try {
            Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
        } catch (PlatformNotSupportedException) {
            // Ignore
        }

        // Reference: audioqueue_thread line 1023
        // "init was successful, alert parent thread and start running..."
        _readySemaphore.Set();

        // Reference: audioqueue_thread line 1025-1059
        // Main run loop
        while (!_shutdown && (_device == null || !_device.ShutdownRequested)) {
            // Reference: audioqueue_thread line 1026
            // CFRunLoopRunInMode(kCFRunLoopDefaultMode, 0.10, 1)
            CoreAudioNativeMethods.CFRunLoopRunInMode(_defaultRunLoopMode, 0.10, 1);
        }

        // Reference: audioqueue_thread line 1061-1064
        // "if (!this->iscapture)" - drain off any pending playback
        if (_device != null) {
            double secs = (((double)_mixBufferSize / sizeof(float)) / channels) / sampleRate * 2.0;
            CoreAudioNativeMethods.CFRunLoopRunInMode(_defaultRunLoopMode, secs, 0);
        }
    }

    /// <summary>
    /// Prepares the AudioQueue.
    /// Reference: prepare_audioqueue (SDL_coreaudio.m line 896)
    /// 
    /// Flow:
    /// 1. Create AudioQueueNewOutput with float PCM format
    /// 2. Set channel layout
    /// 3. Allocate and enqueue audio buffers
    /// 4. Start the AudioQueue
    /// </summary>
    private bool PrepareAudioQueue(int sampleRate, int channels, int bufferFrames) {
        // Reference: prepare_audioqueue line ~896-900
        // Setup AudioStreamBasicDescription
        CoreAudioNativeMethods.AudioStreamBasicDescription strdesc = new() {
            SampleRate = sampleRate,
            FormatId = CoreAudioConstants.AudioFormatLinearPcm,
            // Float LE + Packed (matching SDL's AUDIO_F32LSB path)
            FormatFlags = CoreAudioConstants.LinearPcmFormatFlagIsFloat |
                          CoreAudioConstants.LinearPcmFormatFlagIsPacked,
            ChannelsPerFrame = (uint)channels,
            BitsPerChannel = 32, // float = 32 bits
            FramesPerPacket = 1,
            BytesPerFrame = (uint)(channels * sizeof(float)),
            BytesPerPacket = (uint)(channels * sizeof(float)),
        };

        // Reference: prepare_audioqueue line ~908-910
        // AudioQueueNewOutput with CFRunLoopGetCurrent()
        _outputCallbackDelegate = OutputCallback;
        _callbackHandle = GCHandle.Alloc(_outputCallbackDelegate);

        IntPtr currentRunLoop = CoreAudioNativeMethods.CFRunLoopGetCurrent();

        int result = CoreAudioNativeMethods.AudioQueueNewOutput(
            ref strdesc,
            _outputCallbackDelegate,
            IntPtr.Zero,
            currentRunLoop,
            _defaultRunLoopMode,
            0,
            out _audioQueue);

        if (result != CoreAudioConstants.NoErr) {
            _threadError = $"CoreAudio: AudioQueueNewOutput failed with error {result}";
            return false;
        }

        // Reference: prepare_audioqueue line ~920-944
        // Set channel layout
        CoreAudioNativeMethods.AudioChannelLayout layout = new();
        switch (channels) {
            case 1:
                layout.ChannelLayoutTag = CoreAudioConstants.AudioChannelLayoutTagMono;
                break;
            case 2:
                layout.ChannelLayoutTag = CoreAudioConstants.AudioChannelLayoutTagStereo;
                break;
            case 3:
                layout.ChannelLayoutTag = CoreAudioConstants.AudioChannelLayoutTagDvd4;
                break;
            case 4:
                layout.ChannelLayoutTag = CoreAudioConstants.AudioChannelLayoutTagQuadraphonic;
                break;
            case 5:
                layout.ChannelLayoutTag = CoreAudioConstants.AudioChannelLayoutTagDvd6;
                break;
            case 6:
                layout.ChannelLayoutTag = CoreAudioConstants.AudioChannelLayoutTagDvd12;
                break;
            default:
                _threadError = $"CoreAudio: Unsupported audio channels: {channels}";
                CoreAudioNativeMethods.AudioQueueDispose(_audioQueue, 1);
                _audioQueue = IntPtr.Zero;
                return false;
        }

        if (layout.ChannelLayoutTag != 0) {
            int layoutSize = Marshal.SizeOf<CoreAudioNativeMethods.AudioChannelLayout>();
            IntPtr layoutPtr = Marshal.AllocHGlobal(layoutSize);
            try {
                Marshal.StructureToPtr(layout, layoutPtr, false);
                result = CoreAudioNativeMethods.AudioQueueSetProperty(
                    _audioQueue,
                    CoreAudioConstants.AudioQueuePropertyChannelLayout,
                    layoutPtr,
                    (uint)layoutSize);
                // Ignore errors - not critical (SDL does CHECK_RESULT but continues)
            } finally {
                Marshal.FreeHGlobal(layoutPtr);
            }
        }

        // Reference: prepare_audioqueue line ~956-970
        // Calculate number of audio buffers
        // "Make sure we can feed the device a minimum amount of time"
        uint bufferSizeBytes = (uint)(bufferFrames * channels * sizeof(float));
        double msecs = ((double)bufferFrames / sampleRate) * 1000.0;
        int numAudioBuffers = 2;

        if (msecs < CoreAudioConstants.MinimumAudioBufferTimeMs) {
            // Use more buffers if we have a VERY small sample set
            numAudioBuffers = (int)(Math.Ceiling(CoreAudioConstants.MinimumAudioBufferTimeMs / msecs) * 2);
        }

        _audioBuffers = new IntPtr[numAudioBuffers];

        for (int i = 0; i < numAudioBuffers; i++) {
            result = CoreAudioNativeMethods.AudioQueueAllocateBuffer(
                _audioQueue,
                bufferSizeBytes,
                out _audioBuffers[i]);

            if (result != CoreAudioConstants.NoErr) {
                _threadError = $"CoreAudio: AudioQueueAllocateBuffer failed with error {result}";
                CoreAudioNativeMethods.AudioQueueDispose(_audioQueue, 1);
                _audioQueue = IntPtr.Zero;
                return false;
            }

            // Fill with silence and set size
            // Reference: prepare_audioqueue line ~967
            unsafe {
                CoreAudioNativeMethods.AudioQueueBuffer* bufPtr =
                    (CoreAudioNativeMethods.AudioQueueBuffer*)_audioBuffers[i];
                new Span<byte>(bufPtr->AudioData.ToPointer(), (int)bufPtr->AudioDataBytesCapacity).Clear();
                bufPtr->AudioDataByteSize = bufPtr->AudioDataBytesCapacity;
            }

            // Enqueue the buffer
            result = CoreAudioNativeMethods.AudioQueueEnqueueBuffer(
                _audioQueue, _audioBuffers[i], 0, IntPtr.Zero);

            if (result != CoreAudioConstants.NoErr) {
                _threadError = $"CoreAudio: AudioQueueEnqueueBuffer failed with error {result}";
                CoreAudioNativeMethods.AudioQueueDispose(_audioQueue, 1);
                _audioQueue = IntPtr.Zero;
                return false;
            }
        }

        // Reference: prepare_audioqueue line ~972
        // Start the AudioQueue
        result = CoreAudioNativeMethods.AudioQueueStart(_audioQueue, IntPtr.Zero);
        if (result != CoreAudioConstants.NoErr) {
            _threadError = $"CoreAudio: AudioQueueStart failed with error {result}";
            CoreAudioNativeMethods.AudioQueueDispose(_audioQueue, 1);
            _audioQueue = IntPtr.Zero;
            return false;
        }

        return true;
    }

    /// <summary>
    /// The AudioQueue output callback.
    /// Reference: outputCallback (SDL_coreaudio.m line 461)
    /// 
    /// This is called by CoreAudio when a buffer has been consumed and needs refilling.
    /// The callback directly invokes the user callback to get audio data.
    /// 
    /// SDL flow (non-stream path, which is what we use):
    /// 1. Lock mixer_lock
    /// 2. While remaining bytes in buffer:
    ///    a. If bufferOffset >= bufferSize, call user callback to fill mix buffer
    ///    b. Copy from mix buffer to AudioQueue buffer
    /// 3. Enqueue the buffer back
    /// 4. Unlock mixer_lock
    /// </summary>
    private void OutputCallback(IntPtr inUserData, IntPtr inAudioQueue, IntPtr inBuffer) {
        // Reference: outputCallback line 463-466
        // Check shutdown before and after lock
        if (_device == null || _device.ShutdownRequested) {
            return;
        }

        lock (_mixerLock) {
            if (_device.ShutdownRequested) {
                return;
            }

            unsafe {
                CoreAudioNativeMethods.AudioQueueBuffer* bufPtr =
                    (CoreAudioNativeMethods.AudioQueueBuffer*)inBuffer;

                uint remaining = bufPtr->AudioDataBytesCapacity;
                byte* ptr = (byte*)bufPtr->AudioData;

                // Reference: outputCallback line 501-518 (non-stream path)
                while (remaining > 0) {
                    if (_mixBufferOffset >= _mixBufferSize) {
                        // Generate the data via the user callback
                        // Reference: outputCallback line 504-505
                        _device.FillAudioBuffer(_mixBuffer, _mixBufferSize);
                        _mixBufferOffset = 0;
                    }

                    uint len = (uint)(_mixBufferSize - _mixBufferOffset);
                    if (len > remaining) {
                        len = remaining;
                    }

                    // Reference: outputCallback line 512-513
                    Buffer.MemoryCopy(
                        ((byte*)_mixBuffer + _mixBufferOffset),
                        ptr,
                        remaining,
                        len);

                    ptr += len;
                    remaining -= len;
                    _mixBufferOffset += (int)len;
                }

                // Reference: outputCallback line 487-489
                // Enqueue the buffer back and set its size
                CoreAudioNativeMethods.AudioQueueEnqueueBuffer(
                    inAudioQueue, inBuffer, 0, IntPtr.Zero);
                bufPtr->AudioDataByteSize = bufPtr->AudioDataBytesCapacity;
            }
        }
    }
}
