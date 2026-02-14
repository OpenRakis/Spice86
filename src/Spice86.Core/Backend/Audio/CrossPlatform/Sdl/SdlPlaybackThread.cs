namespace Spice86.Core.Backend.Audio.CrossPlatform.Sdl;

using System;
using System.Threading;

/// <summary>
/// Shared audio playback thread iteration logic.
/// Reference: SDL_RunAudio from SDL_audio.c lines 669-800
/// </summary>
internal static class SdlPlaybackThread {
    /// <summary>
    /// Performs one iteration of the audio playback loop.
    /// Reference: SDL_RunAudio main loop body (lines 704-790)
    /// </summary>
    internal static bool Iterate(SdlAudioDevice device, ISdlAudioDriver driver, object deviceLock) {
        if (IsShutdownRequested(device)) {
            return false;
        }

        // Reference: SDL_audio.c SDL_RunAudio lines 704-790
        // SDL flow: GetDeviceBuf -> lock -> callback/silence -> unlock -> PlayDevice -> WaitDevice
        // GetDeviceBuf now internally handles BUFFER_TOO_LARGE retries (matching SDL)
        int bufferSize = device.BufferSizeBytes;
        IntPtr deviceBuffer = driver.GetDeviceBuffer(device, out int bufferBytes);
        if (bufferBytes < 0) {
            return false;
        }

        if (deviceBuffer == IntPtr.Zero) {
            // Device isn't happy - sleep for buffer duration and retry
            // Reference: SDL_RunAudio lines 783-786
            int delayMs = (device.SampleFrames * 1000) / device.ObtainedSpec.SampleRate;
            if (delayMs > 0) {
                Thread.Sleep(delayMs);
            }
            return true;
        }

        int clampedBytes = Math.Min(bufferBytes, bufferSize);

        // Reference: SDL_RunAudio locks only around the callback fill
        // SDL_LockMutex(device->mixer_lock)
        lock (deviceLock) {
            device.FillAudioBuffer(deviceBuffer, clampedBytes);
        }
        // SDL_UnlockMutex(device->mixer_lock)

        // Reference: SDL_RunAudio calls PlayDevice then WaitDevice
        // Lines 788-789: current_audio.impl.PlayDevice(device);
        //                current_audio.impl.WaitDevice(device);
        if (!driver.PlayDevice(device, deviceBuffer, clampedBytes)) {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Performs shutdown drain and deinit.
    /// Reference: SDL_RunAudio lines 793-800
    /// </summary>
    internal static void Shutdown(SdlAudioDevice device, ISdlAudioDriver driver) {
        int frames = device.BufferSizeBytes / (device.ObtainedSpec.Channels * sizeof(float));
        int delayMs = ((frames * 1000) / device.ObtainedSpec.SampleRate) * 2;
        if (delayMs > 100) {
            delayMs = 100;
        }

        Thread.Sleep(delayMs);
        driver.ThreadDeinit(device);
    }

    private static bool IsShutdownRequested(SdlAudioDevice device) {
        return device.ShutdownRequested;
    }
}
