namespace Spice86.Core.Emulator.Devices.Sound;

using System;
using System.IO;

/// <summary>
/// Loads a 22 050 Hz mono 16-bit PCM WAVE file into a normalised
/// <c>float[]</c> array (samples in [-1, 1]).
/// </summary>
/// <remarks>
/// Only the minimal RIFF/WAVE subset required for the disk-noise samples is
/// supported: PCM (AudioFormat = 1), mono, 22 050 Hz, 16-bit, standard "fmt "
/// and "data" chunks.  Files that do not match are rejected silently and an
/// empty array is returned so the caller can treat absence of samples as
/// silence rather than an error.
/// </remarks>
internal static class WavPcmLoader {
    private const int ExpectedSampleRate = 22050;
    private const int ExpectedChannels = 1;
    private const int ExpectedBitsPerSample = 16;

    /// <summary>
    /// Attempts to load <paramref name="path"/> as a 22 050 Hz mono 16-bit
    /// PCM WAVE file.
    /// </summary>
    /// <param name="path">Absolute or relative host path to the WAV file.</param>
    /// <returns>
    /// Normalised float samples in [-1, 1], or an empty array when the file
    /// is missing, unreadable, or does not match the expected format.
    /// </returns>
    internal static float[] TryLoad(string path) {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
            return Array.Empty<float>();
        }

        try {
            return ParseWav(path);
        } catch (IOException) {
            return Array.Empty<float>();
        } catch (InvalidDataException) {
            return Array.Empty<float>();
        }
    }

    private static float[] ParseWav(string path) {
        using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using BinaryReader reader = new(fs);

        // RIFF header
        string riff = ReadFourCC(reader);
        if (riff != "RIFF") {
            return Array.Empty<float>();
        }

        reader.ReadUInt32(); // chunk size — ignored
        string wave = ReadFourCC(reader);
        if (wave != "WAVE") {
            return Array.Empty<float>();
        }

        ushort audioFormat = 0;
        ushort channels = 0;
        uint sampleRate = 0;
        ushort bitsPerSample = 0;
        byte[]? pcmData = null;

        // Walk sub-chunks until we have both "fmt " and "data"
        while (fs.Position < fs.Length - 8) {
            string chunkId = ReadFourCC(reader);
            uint chunkSize = reader.ReadUInt32();
            long nextChunk = fs.Position + chunkSize;

            if (chunkId == "fmt ") {
                audioFormat = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                sampleRate = reader.ReadUInt32();
                reader.ReadUInt32(); // byte rate
                reader.ReadUInt16(); // block align
                bitsPerSample = reader.ReadUInt16();
            } else if (chunkId == "data") {
                pcmData = reader.ReadBytes((int)chunkSize);
            }

            // Skip any remaining bytes in this chunk (handles extended fmt, LIST, etc.)
            if (fs.Position < nextChunk) {
                fs.Seek(nextChunk, SeekOrigin.Begin);
            }
        }

        if (audioFormat != 1
            || channels != ExpectedChannels
            || sampleRate != ExpectedSampleRate
            || bitsPerSample != ExpectedBitsPerSample
            || pcmData == null
            || pcmData.Length < 2) {
            return Array.Empty<float>();
        }

        int sampleCount = pcmData.Length / 2;
        float[] samples = new float[sampleCount];
        const float scale = 1.0f / 32768.0f;
        for (int i = 0; i < sampleCount; i++) {
            short raw = (short)(pcmData[i * 2] | (pcmData[i * 2 + 1] << 8));
            samples[i] = raw * scale;
        }

        return samples;
    }

    private static string ReadFourCC(BinaryReader reader) {
        byte[] bytes = reader.ReadBytes(4);
        return System.Text.Encoding.ASCII.GetString(bytes);
    }
}
