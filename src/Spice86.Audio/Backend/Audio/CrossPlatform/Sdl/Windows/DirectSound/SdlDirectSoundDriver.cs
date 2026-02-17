namespace Spice86.Audio.Backend.Audio.CrossPlatform.Sdl.Windows.DirectSound;

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

using Spice86.Audio.Backend.Audio.CrossPlatform.Sdl;

[SupportedOSPlatform("windows")]
internal sealed class SdlDirectSoundDriver : ISdlAudioDriver {
    private const uint NumChunks = 8;

    private IDirectSound8? _sound;
    private IDirectSoundBuffer? _mixBuffer;
    private uint _lastChunk;
    private uint _bufferSize;
    private uint _numBuffers;
    private uint _totalBufferBytes;
    private IntPtr _lockedBuffer;

    public bool OpenDevice(SdlAudioDevice device, AudioSpec desiredSpec, out AudioSpec obtainedSpec, out int sampleFrames, out string? error) {
        obtainedSpec = desiredSpec;
        sampleFrames = desiredSpec.BufferFrames;
        error = null;

        int hr = SdlDirectSoundNative.DirectSoundCreate8(IntPtr.Zero, out IDirectSound8 sound, IntPtr.Zero);
        if (hr != SdlDirectSoundConstants.DsOk) {
            error = $"DirectSoundCreate8 failed: 0x{hr:X8}";
            return false;
        }

        _sound = sound;

        IntPtr window = SdlDirectSoundNative.GetDesktopWindow();
        hr = _sound.SetCooperativeLevel(window, SdlDirectSoundConstants.DssclNormal);
        if (hr != SdlDirectSoundConstants.DsOk) {
            error = $"DirectSound SetCooperativeLevel failed: 0x{hr:X8}";
            return false;
        }

        _numBuffers = NumChunks;
        _bufferSize = (uint)(sampleFrames * desiredSpec.Channels * sizeof(float));
        _totalBufferBytes = _bufferSize * _numBuffers;

        WaveFormatEx format = WaveFormatEx.CreateIeeeFloat(desiredSpec.SampleRate, desiredSpec.Channels);
        IntPtr formatPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WaveFormatEx>());
        try {
            Marshal.StructureToPtr(format, formatPtr, false);

            DsBufferDesc desc = new DsBufferDesc {
                Size = (uint)Marshal.SizeOf<DsBufferDesc>(),
                Flags = SdlDirectSoundConstants.DsbcapsGetCurrentPosition2 | SdlDirectSoundConstants.DsbcapsGlobalFocus,
                BufferBytes = _totalBufferBytes,
                Reserved = 0,
                Format = formatPtr,
                Guid3DAlgorithm = Guid.Empty
            };

            hr = _sound.CreateSoundBuffer(ref desc, out IDirectSoundBuffer buffer, IntPtr.Zero);
            if (hr != SdlDirectSoundConstants.DsOk) {
                error = $"DirectSound CreateSoundBuffer failed: 0x{hr:X8}";
                return false;
            }

            _mixBuffer = buffer;
            hr = _mixBuffer.SetFormat(formatPtr);
            if (hr != SdlDirectSoundConstants.DsOk) {
                error = $"DirectSound SetFormat failed: 0x{hr:X8}";
                return false;
            }

            if (!SilenceBuffer(_totalBufferBytes)) {
                error = "DirectSound failed to silence buffer";
                return false;
            }
        } finally {
            Marshal.FreeHGlobal(formatPtr);
        }

        obtainedSpec = new AudioSpec {
            SampleRate = desiredSpec.SampleRate,
            Channels = desiredSpec.Channels,
            BufferFrames = sampleFrames,
            Callback = desiredSpec.Callback,
            PostmixCallback = desiredSpec.PostmixCallback
        };

        return true;
    }

    public void CloseDevice(SdlAudioDevice device) {
        if (_mixBuffer != null) {
            _mixBuffer.Stop();
            Marshal.ReleaseComObject(_mixBuffer);
            _mixBuffer = null;
        }

        if (_sound != null) {
            Marshal.ReleaseComObject(_sound);
            _sound = null;
        }
    }

    public void WaitDevice(SdlAudioDevice device) {
        if (_mixBuffer == null) {
            device.SetDeviceDisconnected();
            return;
        }

        // Reference: DSOUND_WaitDevice (SDL_directsound.c)
        // Semi-busy wait, since we have no way of getting play notification
        // on a primary mixing buffer located in hardware (DirectX 5.0)
        int hr = _mixBuffer.GetCurrentPosition(out _, out uint cursor);
        if (hr != SdlDirectSoundConstants.DsOk) {
            if (hr == SdlDirectSoundConstants.DsErrBufferLost) {
                _mixBuffer.Restore();
            }
            return;
        }

        while ((cursor / _bufferSize) == _lastChunk) {
            // Try to restore a lost sound buffer
            hr = _mixBuffer.GetStatus(out uint status);
            if ((status & SdlDirectSoundConstants.DsbstatusBufferLost) != 0) {
                _mixBuffer.Restore();
                _mixBuffer.GetStatus(out status);
                if ((status & SdlDirectSoundConstants.DsbstatusBufferLost) != 0) {
                    break;
                }
            }
            if ((status & SdlDirectSoundConstants.DsbstatusPlaying) == 0) {
                hr = _mixBuffer.Play(0, 0, SdlDirectSoundConstants.DsbplayLooping);
                if (hr == SdlDirectSoundConstants.DsOk) {
                    continue;
                }
                return;
            }

            // Find out where we are playing
            hr = _mixBuffer.GetCurrentPosition(out _, out cursor);
            if (hr != SdlDirectSoundConstants.DsOk) {
                return;
            }
        }
    }

    public IntPtr GetDeviceBuf(SdlAudioDevice device) {
        if (_mixBuffer == null) {
            device.SetDeviceDisconnected();
            return IntPtr.Zero;
        }

        // Reference: DSOUND_GetDeviceBuf (SDL_directsound.c)
        // Figure out which blocks to fill next
        _lockedBuffer = IntPtr.Zero;
        int hr = _mixBuffer.GetCurrentPosition(out _, out uint cursor);
        if (hr == SdlDirectSoundConstants.DsErrBufferLost) {
            _mixBuffer.Restore();
            hr = _mixBuffer.GetCurrentPosition(out _, out cursor);
        }

        if (hr != SdlDirectSoundConstants.DsOk) {
            device.SetDeviceDisconnected();
            return IntPtr.Zero;
        }

        cursor /= _bufferSize;
        _lastChunk = cursor;
        cursor = (cursor + 1) % _numBuffers;
        cursor *= _bufferSize;

        // Lock the audio buffer
        hr = _mixBuffer.Lock(cursor, _bufferSize, out IntPtr ptr1, out uint bytes1, out IntPtr ptr2, out uint bytes2, 0);
        if (hr == SdlDirectSoundConstants.DsErrBufferLost) {
            _mixBuffer.Restore();
            hr = _mixBuffer.Lock(cursor, _bufferSize, out ptr1, out bytes1, out ptr2, out bytes2, 0);
        }

        if (hr != SdlDirectSoundConstants.DsOk) {
            device.SetDeviceDisconnected();
            return IntPtr.Zero;
        }

        _lockedBuffer = ptr1;
        return ptr1;
    }

    public void PlayDevice(SdlAudioDevice device) {
        if (_mixBuffer == null) {
            device.SetDeviceDisconnected();
            return;
        }

        // Reference: DSOUND_PlayDevice (SDL_directsound.c)
        // Unlock the buffer, allowing it to play
        if (_lockedBuffer != IntPtr.Zero) {
            _mixBuffer.Unlock(_lockedBuffer, _bufferSize, IntPtr.Zero, 0);
            _lockedBuffer = IntPtr.Zero;
        }
    }

    public void ThreadInit(SdlAudioDevice device) {
        // DirectSound has no ThreadInit callback in SDL.
    }

    public void ThreadDeinit(SdlAudioDevice device) {
        // DirectSound has no ThreadDeinit callback in SDL.
    }

    private bool SilenceBuffer(uint totalBytes) {
        if (_mixBuffer == null) {
            return false;
        }

        int hr = _mixBuffer.Lock(0, totalBytes, out IntPtr ptr1, out uint bytes1, out IntPtr ptr2, out uint bytes2, SdlDirectSoundConstants.DsblockEntireBuffer);
        if (hr != SdlDirectSoundConstants.DsOk) {
            return false;
        }

        unsafe {
            Span<byte> span1 = new Span<byte>(ptr1.ToPointer(), (int)bytes1);
            span1.Clear();
            if (ptr2 != IntPtr.Zero && bytes2 > 0) {
                Span<byte> span2 = new Span<byte>(ptr2.ToPointer(), (int)bytes2);
                span2.Clear();
            }
        }

        _mixBuffer.Unlock(ptr1, bytes1, ptr2, bytes2);
        return true;
    }
}
