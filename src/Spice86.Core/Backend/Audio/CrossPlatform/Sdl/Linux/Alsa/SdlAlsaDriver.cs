namespace Spice86.Core.Backend.Audio.CrossPlatform.Sdl.Linux.Alsa;

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

/// <summary>
/// ALSA audio driver implementing ISdlAudioDriver.
/// This is an exact port of SDL_alsa_audio.c (SDL2) to C#.
/// 
/// Reference: SDL_alsa_audio.c
/// - ALSA_OpenDevice (line 593): opens PCM device, configures hw/sw params, allocates mix buffer
/// - ALSA_CloseDevice (line 468): drains and closes PCM, frees mix buffer
/// - ALSA_WaitDevice (line 245): waits until PCM has room for a full period (non-blocking path)
/// - ALSA_PlayDevice (line 373): writes interleaved frames to PCM with recovery
/// - ALSA_GetDeviceBuf (line 415): returns the mix buffer pointer
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class SdlAlsaDriver : ISdlAudioDriver {
    private IntPtr _pcmHandle;
    private IntPtr _mixBuffer;
    private int _mixBufferBytes;
    private int _sampleFrames;
    private int _channels;
    private int _frameSize;

    /// <summary>
    /// Opens the ALSA PCM device.
    /// Reference: ALSA_OpenDevice (SDL_alsa_audio.c line 593)
    /// 
    /// Flow:
    /// 1. snd_pcm_open with "default" device, SND_PCM_NONBLOCK
    /// 2. Configure hw params: access=RW_INTERLEAVED, format=FLOAT_LE, channels, rate
    /// 3. Set buffer size via ALSA_set_buffer_size (period size + periods)
    /// 4. Configure sw params: avail_min=samples, start_threshold=1
    /// 5. Set to blocking mode for playback
    /// 6. Allocate mix buffer
    /// </summary>
    public bool OpenDevice(SdlAudioDevice device, AudioSpec desiredSpec, out AudioSpec obtainedSpec, out int sampleFrames, out string? error) {
        obtainedSpec = desiredSpec;
        sampleFrames = 0;
        error = null;

        // Reference: ALSA_OpenDevice line 619-622
        // Open the audio device - name depends on channels
        // get_audio_device returns "plug:surround51" for 6ch, "plug:surround40" for 4ch, else "default"
        string deviceName = GetAudioDevice(desiredSpec.Channels);

        int status = AlsaNativeMethods.snd_pcm_open(
            out _pcmHandle,
            deviceName,
            AlsaConstants.SndPcmStreamPlayback,
            AlsaConstants.SndPcmNonblock);

        if (status < 0) {
            error = $"ALSA: Couldn't open audio device: {AlsaNativeMethods.GetErrorString(status)}";
            return false;
        }

        // Allocate hw params
        // Reference: ALSA_OpenDevice line 630-631
        IntPtr hwparams = IntPtr.Zero;
        status = AlsaNativeMethods.snd_pcm_hw_params_malloc(out hwparams);
        if (status < 0) {
            error = $"ALSA: Couldn't allocate hw params: {AlsaNativeMethods.GetErrorString(status)}";
            CleanupPcm();
            return false;
        }

        try {
            // Reference: ALSA_OpenDevice line 632-634
            status = AlsaNativeMethods.snd_pcm_hw_params_any(_pcmHandle, hwparams);
            if (status < 0) {
                error = $"ALSA: Couldn't get hardware config: {AlsaNativeMethods.GetErrorString(status)}";
                return false;
            }

            // Reference: ALSA_OpenDevice line 637-639
            // SDL only uses interleaved sample output
            status = AlsaNativeMethods.snd_pcm_hw_params_set_access(
                _pcmHandle, hwparams, AlsaConstants.SndPcmAccessRwInterleaved);
            if (status < 0) {
                error = $"ALSA: Couldn't set interleaved access: {AlsaNativeMethods.GetErrorString(status)}";
                return false;
            }

            // Reference: ALSA_OpenDevice line 642-681
            // We always use float LE format (matching our AudioSpec which is float)
            // This corresponds to the AUDIO_F32LSB / SND_PCM_FORMAT_FLOAT_LE path in SDL
            status = AlsaNativeMethods.snd_pcm_hw_params_set_format(
                _pcmHandle, hwparams, AlsaConstants.SndPcmFormatFloatLe);
            if (status < 0) {
                error = $"ALSA: Couldn't set float format: {AlsaNativeMethods.GetErrorString(status)}";
                return false;
            }

            // Reference: ALSA_OpenDevice line 700-710
            // Set the number of channels
            uint channels = (uint)desiredSpec.Channels;
            status = AlsaNativeMethods.snd_pcm_hw_params_set_channels(_pcmHandle, hwparams, channels);
            if (status < 0) {
                // Try to get whatever channels the hardware supports
                status = AlsaNativeMethods.snd_pcm_hw_params_get_channels(hwparams, out channels);
                if (status < 0) {
                    error = "ALSA: Couldn't set audio channels";
                    return false;
                }
            }

            // Reference: ALSA_OpenDevice line 713-718
            // Set the audio rate
            uint rate = (uint)desiredSpec.SampleRate;
            status = AlsaNativeMethods.snd_pcm_hw_params_set_rate_near(_pcmHandle, hwparams, ref rate, IntPtr.Zero);
            if (status < 0) {
                error = $"ALSA: Couldn't set audio frequency: {AlsaNativeMethods.GetErrorString(status)}";
                return false;
            }

            // Reference: ALSA_OpenDevice line 721-724 -> ALSA_set_buffer_size
            // Set the buffer size (period size + periods)
            ulong persize = (ulong)desiredSpec.BufferFrames;
            if (!SetBufferSize(_pcmHandle, hwparams, ref persize, out error)) {
                return false;
            }

            // Reference: ALSA_OpenDevice line 727-744
            // Set the software parameters
            IntPtr swparams = IntPtr.Zero;
            status = AlsaNativeMethods.snd_pcm_sw_params_malloc(out swparams);
            if (status < 0) {
                error = $"ALSA: Couldn't allocate sw params: {AlsaNativeMethods.GetErrorString(status)}";
                return false;
            }

            try {
                status = AlsaNativeMethods.snd_pcm_sw_params_current(_pcmHandle, swparams);
                if (status < 0) {
                    error = $"ALSA: Couldn't get software config: {AlsaNativeMethods.GetErrorString(status)}";
                    return false;
                }

                // Reference: ALSA_OpenDevice line 733-736
                status = AlsaNativeMethods.snd_pcm_sw_params_set_avail_min(_pcmHandle, swparams, persize);
                if (status < 0) {
                    error = $"ALSA: Couldn't set minimum available samples: {AlsaNativeMethods.GetErrorString(status)}";
                    return false;
                }

                // Reference: ALSA_OpenDevice line 737-741
                status = AlsaNativeMethods.snd_pcm_sw_params_set_start_threshold(_pcmHandle, swparams, 1);
                if (status < 0) {
                    error = $"ALSA: Couldn't set start threshold: {AlsaNativeMethods.GetErrorString(status)}";
                    return false;
                }

                status = AlsaNativeMethods.snd_pcm_sw_params(_pcmHandle, swparams);
                if (status < 0) {
                    error = $"ALSA: Couldn't set software audio parameters: {AlsaNativeMethods.GetErrorString(status)}";
                    return false;
                }
            } finally {
                AlsaNativeMethods.snd_pcm_sw_params_free(swparams);
            }

            // Reference: ALSA_OpenDevice line 749-756
            // Calculate final parameters
            _sampleFrames = (int)persize;
            _channels = (int)channels;
            _frameSize = _channels * sizeof(float); // float LE = 4 bytes per sample
            _mixBufferBytes = _sampleFrames * _frameSize;

            // Allocate mixing buffer
            // Reference: ALSA_OpenDevice line 758-762
            _mixBuffer = Marshal.AllocHGlobal(_mixBufferBytes);
            unsafe {
                Span<byte> mixSpan = new Span<byte>(_mixBuffer.ToPointer(), _mixBufferBytes);
                mixSpan.Clear(); // silence = 0 for float
            }

            // Reference: ALSA_OpenDevice line 766-768
            // Set to blocking mode for playback (SDL_ALSA_NON_BLOCKING is 0 by default)
            AlsaNativeMethods.snd_pcm_nonblock(_pcmHandle, 0);

            obtainedSpec = new AudioSpec {
                SampleRate = (int)rate,
                Channels = (int)channels,
                BufferFrames = _sampleFrames,
                Callback = desiredSpec.Callback,
                PostmixCallback = desiredSpec.PostmixCallback
            };
            sampleFrames = _sampleFrames;

            return true;
        } finally {
            AlsaNativeMethods.snd_pcm_hw_params_free(hwparams);
        }
    }

    /// <summary>
    /// Closes the ALSA PCM device.
    /// Reference: ALSA_CloseDevice (SDL_alsa_audio.c line 468)
    /// 
    /// Flow:
    /// 1. Wait for submitted audio to drain (delay = samples*1000/freq * 2, clamped to 100ms)
    /// 2. snd_pcm_close
    /// 3. Free mix buffer
    /// </summary>
    public void CloseDevice(SdlAudioDevice device) {
        if (_pcmHandle != IntPtr.Zero) {
            // Reference: ALSA_CloseDevice line 471-478
            // Wait for the submitted audio to drain
            // ALSA_snd_pcm_drop() can hang, so don't use that.
            int delay = ((device.SampleFrames * 1000) / device.ObtainedSpec.SampleRate) * 2;
            if (delay > 100) {
                delay = 100;
            }
            Thread.Sleep(delay);

            AlsaNativeMethods.snd_pcm_close(_pcmHandle);
            _pcmHandle = IntPtr.Zero;
        }

        if (_mixBuffer != IntPtr.Zero) {
            Marshal.FreeHGlobal(_mixBuffer);
            _mixBuffer = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Waits until ALSA is ready for more data.
    /// Reference: ALSA_WaitDevice (SDL_alsa_audio.c line 245)
    /// 
    /// When SDL_ALSA_NON_BLOCKING is 0 (default), this is a no-op because
    /// snd_pcm_writei in blocking mode already waits. SDL's default ALSA path
    /// uses blocking writes.
    /// </summary>
    public bool WaitDevice(SdlAudioDevice device) {
        // Reference: ALSA_WaitDevice
        // When SDL_ALSA_NON_BLOCKING is 0 (default), this function is empty.
        // The blocking snd_pcm_writei in PlayDevice will handle the wait.
        // This matches SDL's default behavior exactly.
        return true;
    }

    /// <summary>
    /// Gets the device buffer pointer.
    /// Reference: ALSA_GetDeviceBuf (SDL_alsa_audio.c line 415)
    /// Returns this->hidden->mixbuf
    /// </summary>
    public IntPtr GetDeviceBuffer(SdlAudioDevice device, out int bufferBytes) {
        // Reference: ALSA_GetDeviceBuf line 417
        // return this->hidden->mixbuf
        bufferBytes = _mixBufferBytes;
        return _mixBuffer;
    }

    /// <summary>
    /// Writes audio data to ALSA.
    /// Reference: ALSA_PlayDevice (SDL_alsa_audio.c line 373)
    /// 
    /// Flow:
    /// 1. Write interleaved frames via snd_pcm_writei
    /// 2. On -EAGAIN: delay 1ms and retry
    /// 3. On other errors: snd_pcm_recover, retry
    /// 4. On unrecoverable error: return false (disconnected)
    /// 5. On status==0: delay half the remaining time, retry
    /// </summary>
    public bool PlayDevice(SdlAudioDevice device, IntPtr buffer, int bufferBytes) {
        // Reference: ALSA_PlayDevice (SDL_alsa_audio.c line 373-413)
        IntPtr sampleBuf = buffer;
        int frameSize = _frameSize;
        long framesLeft = _sampleFrames;

        // Note: SDL calls swizzle_func here for 6/8 channel layouts.
        // We only support stereo (2 channels) for now, so no swizzle needed.
        // Channel swizzling is only needed for 6ch and 8ch surround sound.

        while (framesLeft > 0 && !device.ShutdownRequested) {
            // Reference: ALSA_PlayDevice line 383-384
            long status = AlsaNativeMethods.snd_pcm_writei(_pcmHandle, sampleBuf, (ulong)framesLeft);

            if (status < 0) {
                if (status == -AlsaConstants.Eagain) {
                    // Reference: ALSA_PlayDevice line 387-391
                    // Apparently snd_pcm_recover() doesn't handle this case -
                    // does it assume snd_pcm_wait() above?
                    Thread.Sleep(1);
                    continue;
                }

                // Reference: ALSA_PlayDevice line 392-401
                int recoverStatus = AlsaNativeMethods.snd_pcm_recover(_pcmHandle, (int)status, 0);
                if (recoverStatus < 0) {
                    // Hmm, not much we can do - abort
                    // Reference: ALSA_PlayDevice line 394-400
                    return false;
                }
                continue;
            } else if (status == 0) {
                // Reference: ALSA_PlayDevice line 402-406
                // No frames were written (no available space in pcm device).
                // Allow other threads to catch up.
                int delay = (int)((framesLeft / 2 * 1000) / device.ObtainedSpec.SampleRate);
                if (delay > 0) {
                    Thread.Sleep(delay);
                }
            }

            // Reference: ALSA_PlayDevice line 408-409
            sampleBuf = IntPtr.Add(sampleBuf, (int)(status * frameSize));
            framesLeft -= status;
        }

        return true;
    }

    /// <summary>
    /// Called at the start of the audio thread.
    /// Reference: SDL_audio.c SDL_RunAudio line 692
    /// SDL_SetThreadPriority(SDL_THREAD_PRIORITY_TIME_CRITICAL)
    /// ALSA has no ThreadInit callback.
    /// </summary>
    public void ThreadInit(SdlAudioDevice device) {
        // Reference: SDL_audio.c SDL_RunAudio line 692
        // SDL sets thread priority to TIME_CRITICAL here.
        // .NET doesn't directly expose real-time priority, but we set highest available.
        try {
            Thread.CurrentThread.Priority = ThreadPriority.Highest;
        } catch (PlatformNotSupportedException) {
            // Ignore on platforms that don't support thread priority changes
        }
    }

    /// <summary>
    /// Called at the end of the audio thread.
    /// ALSA has no ThreadDeinit callback in SDL.
    /// </summary>
    public void ThreadDeinit(SdlAudioDevice device) {
        // No-op for ALSA, matching SDL's default SDL_AudioThreadDeinit_Default
    }

    /// <summary>
    /// Gets the ALSA device name based on channel count.
    /// Reference: get_audio_device() in SDL_alsa_audio.c line 229-242
    /// </summary>
    private static string GetAudioDevice(int channels) {
        // Reference: get_audio_device lines 237-241
        if (channels == 6) {
            return "plug:surround51";
        } else if (channels == 4) {
            return "plug:surround40";
        }
        return "default";
    }

    /// <summary>
    /// Sets the ALSA buffer size (period size and periods).
    /// Reference: ALSA_set_buffer_size (SDL_alsa_audio.c line 538)
    /// 
    /// Flow:
    /// 1. Copy hw params
    /// 2. Set period size near requested
    /// 3. Set periods min to 2 (at least double buffer)
    /// 4. Set periods to first available
    /// 5. Apply hw params
    /// </summary>
    private bool SetBufferSize(IntPtr pcmHandle, IntPtr hwparams, ref ulong persize, out string? error) {
        error = null;
        int status;

        // Reference: ALSA_set_buffer_size line 544-545
        // Copy the hardware parameters for this setup
        IntPtr hwparamsCopy = IntPtr.Zero;
        status = AlsaNativeMethods.snd_pcm_hw_params_malloc(out hwparamsCopy);
        if (status < 0) {
            error = $"ALSA: Couldn't allocate hw params copy: {AlsaNativeMethods.GetErrorString(status)}";
            return false;
        }

        try {
            AlsaNativeMethods.snd_pcm_hw_params_copy(hwparamsCopy, hwparams);

            // Reference: ALSA_set_buffer_size line 548-551
            // Attempt to match the period size to the requested buffer size
            status = AlsaNativeMethods.snd_pcm_hw_params_set_period_size_near(
                pcmHandle, hwparamsCopy, ref persize, IntPtr.Zero);
            if (status < 0) {
                error = $"ALSA: Couldn't set period size: {AlsaNativeMethods.GetErrorString(status)}";
                return false;
            }

            // Reference: ALSA_set_buffer_size line 554-558
            // Need to at least double buffer
            uint periods = 2;
            status = AlsaNativeMethods.snd_pcm_hw_params_set_periods_min(
                pcmHandle, hwparamsCopy, ref periods, IntPtr.Zero);
            if (status < 0) {
                error = $"ALSA: Couldn't set periods min: {AlsaNativeMethods.GetErrorString(status)}";
                return false;
            }

            // Reference: ALSA_set_buffer_size line 560-564
            status = AlsaNativeMethods.snd_pcm_hw_params_set_periods_first(
                pcmHandle, hwparamsCopy, ref periods, IntPtr.Zero);
            if (status < 0) {
                error = $"ALSA: Couldn't set periods first: {AlsaNativeMethods.GetErrorString(status)}";
                return false;
            }

            // Reference: ALSA_set_buffer_size line 567-570
            // "set" the hardware with the desired parameters
            status = AlsaNativeMethods.snd_pcm_hw_params(pcmHandle, hwparamsCopy);
            if (status < 0) {
                error = $"ALSA: Couldn't set hardware params: {AlsaNativeMethods.GetErrorString(status)}";
                return false;
            }

            // Reference: ALSA_set_buffer_size line 572
            // this->spec.samples = persize;
            // persize was updated by set_period_size_near

            return true;
        } finally {
            AlsaNativeMethods.snd_pcm_hw_params_free(hwparamsCopy);
        }
    }

    private void CleanupPcm() {
        if (_pcmHandle != IntPtr.Zero) {
            AlsaNativeMethods.snd_pcm_close(_pcmHandle);
            _pcmHandle = IntPtr.Zero;
        }
    }
}
