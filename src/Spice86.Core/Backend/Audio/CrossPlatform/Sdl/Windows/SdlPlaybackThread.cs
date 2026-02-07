namespace Spice86.Core.Backend.Audio.CrossPlatform.Sdl.Windows;

using System;
using System.Threading;

internal static class SdlPlaybackThread {
    internal static bool Iterate(SdlAudioDevice device, ISdlAudioDriver driver, object deviceLock) {
        if (IsShutdownRequested(device)) {
            return false;
        }

        // Reference: SDL_audio.c SDL_RunAudio
        // SDL does: GetDeviceBuf -> lock -> callback -> unlock -> PlayDevice
        // GetDeviceBuf is called without the lock
        int bufferSize = device.BufferSizeBytes;
        IntPtr deviceBuffer = driver.GetDeviceBuffer(device, out int bufferBytes);
        if (bufferBytes < 0) {
            return false;
        }
        if (bufferBytes == 0) {
            return true;
        }

        if (deviceBuffer == IntPtr.Zero) {
            return false;
        }

        int clampedBytes = Math.Min(bufferBytes, bufferSize);

        // Reference: SDL_RunAudio locks only around the callback fill
        lock (deviceLock) {
            device.FillAudioBuffer(deviceBuffer, clampedBytes);
        }

        // Reference: SDL_RunAudio calls PlayDevice (ReleaseBuffer) outside the lock
        if (!driver.PlayDevice(device, deviceBuffer, clampedBytes)) {
            return false;
        }

        return true;
    }

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
