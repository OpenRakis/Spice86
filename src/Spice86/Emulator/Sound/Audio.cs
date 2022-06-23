namespace Spice86.Emulator.Sound;

using System;
using System.Runtime.Versioning;
using System.Threading;

using TinyAudio;

internal static class Audio {
    private static bool _sdlAudioInitialized = false;

    public static AudioPlayer? CreatePlayer(bool useCallback = false) {
        if (OperatingSystem.IsWindows()) {
            return WasapiAudioPlayer.Create(TimeSpan.FromSeconds(0.25), useCallback);
        } else {
            //var openAlAudioPlayer = OpenAlAudioPlayer.Create(TimeSpan.FromSeconds(0.25), useCallback);
            //_sdlAudioInitialized = openAlAudioPlayer is not null;
            //return openAlAudioPlayer;
            return null;
        }
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
