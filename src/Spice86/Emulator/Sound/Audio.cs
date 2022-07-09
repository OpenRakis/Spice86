namespace Spice86.Emulator.Sound;

using Backend.Audio.OpenAl;

using System;
using System.Runtime.Versioning;
using System.Threading;

using Spice86.Backend.Audio.OpenAl;

internal static class Audio {
    public static AudioPlayer? CreatePlayer() {
        if (OperatingSystem.IsBrowser()) {
            return null;
        }
        var xplatAudioPlayer = OpenAlAudioPlayer.Create();
        return xplatAudioPlayer;
    }

    public static void WriteFullBuffer(AudioPlayer player, ReadOnlySpan<float> buffer) {
        ReadOnlySpan<float> writeBuffer = buffer;

        while (true) {
            int count = (int)player.WriteData(writeBuffer);
            writeBuffer = writeBuffer[count..];
            if (writeBuffer.IsEmpty) {
                return;
            }
        }
    }
    public static void WriteFullBuffer(AudioPlayer player, ReadOnlySpan<short> buffer) {
        ReadOnlySpan<short> writeBuffer = buffer;

        while (true) {
            int count = (int)player.WriteData(writeBuffer);
            writeBuffer = writeBuffer[count..];
            if (writeBuffer.IsEmpty) {
                return;
            }
        }
    }
    public static void WriteFullBuffer(AudioPlayer player, ReadOnlySpan<byte> buffer) {
        ReadOnlySpan<byte> writeBuffer = buffer;

        float[]? floatArray = new float[writeBuffer.Length];

        for (int i = 0; i < writeBuffer.Length; i++) {
            floatArray[i] = writeBuffer[i];
        }

        var span = new ReadOnlySpan<float>(floatArray);

        while (true) {
            int count = (int)player.WriteData(span);
            writeBuffer = writeBuffer[count..];
            if (writeBuffer.IsEmpty) {
                return;
            }
        }
    }
}
