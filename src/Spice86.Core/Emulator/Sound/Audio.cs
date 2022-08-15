using Spice86.Core.Backend.Audio.PortAudio;

namespace Spice86.Core.Emulator.Sound;

using Spice86.Core.Backend.Audio;

using System;

internal static class Audio {
    public static AudioPlayer? CreatePlayer() {
        if (OperatingSystem.IsBrowser()) {
            return null;
        }
        if (OperatingSystem.IsWindows()) {
            return WasapiAudioPlayer.Create(TimeSpan.FromSeconds(0.25));
        } else {
            return PortAudioPlayer.Create();
        }
    }

    public static void WriteFullBuffer(AudioPlayer player, Span<float> buffer) {
        Span<float> writeBuffer = buffer;

        while (true) {
            int count = player.WriteData(writeBuffer);
            writeBuffer = writeBuffer[count..];
            if (writeBuffer.IsEmpty) {
                return;
            }
            Thread.Sleep(1);
        }
    }
    public static void WriteFullBuffer(AudioPlayer player, Span<short> buffer) {
        Span<short> writeBuffer = buffer;

        while (true) {
            int count = player.WriteData(writeBuffer);
            writeBuffer = writeBuffer[count..];
            if (writeBuffer.IsEmpty) {
                return;
            }
            Thread.Sleep(1);
        }
    }
    public static void WriteFullBuffer(AudioPlayer player, Span<byte> buffer) {
        Span<byte> writeBuffer = buffer;

        float[]? floatArray = new float[writeBuffer.Length];

        for (int i = 0; i < writeBuffer.Length; i++) {
            floatArray[i] = writeBuffer[i];
        }

        Span<float> span = new Span<float>(floatArray);

        while (true) {
            int count = player.WriteData(span);
            writeBuffer = writeBuffer[count..];
            if (writeBuffer.IsEmpty) {
                return;
            }
            Thread.Sleep(1);
        }
    }
}
