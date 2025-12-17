namespace Spice86.Tests.Emulator.Devices.Sound;

using Spice86.Libs.Sound.Common;
using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Support for WAV audio file format for audio output validation.
/// Implements standard PCM WAV file writing and reading for test comparisons.
/// </summary>
public static class WavFileFormat {
    /// <summary>
    /// Write audio frames to a WAV file.
    /// </summary>
    /// <param name="filePath">Output file path</param>
    /// <param name="frames">Audio frames to write</param>
    /// <param name="sampleRate">Sample rate in Hz</param>
    public static void WriteWavFile(string filePath, List<AudioFrame> frames, int sampleRate) {
        using FileStream fs = new(filePath, FileMode.Create, FileAccess.Write);
        using BinaryWriter writer = new(fs);
        
        int channels = 2; // Stereo
        int bitsPerSample = 16;
        int bytesPerSample = bitsPerSample / 8;
        int blockAlign = channels * bytesPerSample;
        int dataSize = frames.Count * blockAlign;
        
        // Write RIFF header
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize); // File size - 8
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        
        // Write fmt chunk
        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16); // Chunk size
        writer.Write((ushort)1); // Audio format (1 = PCM)
        writer.Write((ushort)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * blockAlign); // Byte rate
        writer.Write((ushort)blockAlign);
        writer.Write((ushort)bitsPerSample);
        
        // Write data chunk
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);
        
        // Write audio samples
        foreach (AudioFrame frame in frames) {
            // Convert float (-1.0 to 1.0) to int16
            short leftSample = FloatToInt16(frame.Left);
            short rightSample = FloatToInt16(frame.Right);
            
            writer.Write(leftSample);
            writer.Write(rightSample);
        }
    }
    
    /// <summary>
    /// Read audio frames from a WAV file.
    /// </summary>
    /// <param name="filePath">Input file path</param>
    /// <param name="sampleRate">Output sample rate</param>
    /// <returns>List of audio frames</returns>
    public static List<AudioFrame> ReadWavFile(string filePath, out int sampleRate) {
        if (!File.Exists(filePath)) {
            throw new FileNotFoundException($"WAV file not found: {filePath}");
        }
        
        using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read);
        using BinaryReader reader = new(fs);
        
        // Read RIFF header
        string riff = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (riff != "RIFF") {
            throw new InvalidDataException($"Invalid WAV file: expected RIFF, got {riff}");
        }
        
        int fileSize = reader.ReadInt32();
        string wave = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (wave != "WAVE") {
            throw new InvalidDataException($"Invalid WAV file: expected WAVE, got {wave}");
        }
        
        // Read fmt chunk
        string fmt = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (fmt != "fmt ") {
            throw new InvalidDataException($"Invalid WAV file: expected 'fmt ', got {fmt}");
        }
        
        int fmtSize = reader.ReadInt32();
        ushort audioFormat = reader.ReadUInt16();
        ushort channels = reader.ReadUInt16();
        sampleRate = reader.ReadInt32();
        int byteRate = reader.ReadInt32();
        ushort blockAlign = reader.ReadUInt16();
        ushort bitsPerSample = reader.ReadUInt16();
        
        // Skip any extra fmt data
        if (fmtSize > 16) {
            reader.ReadBytes(fmtSize - 16);
        }
        
        // Read data chunk
        string data = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (data != "data") {
            throw new InvalidDataException($"Invalid WAV file: expected 'data', got {data}");
        }
        
        int dataSize = reader.ReadInt32();
        
        // Read audio samples
        List<AudioFrame> frames = new();
        int samplesPerChannel = dataSize / (channels * (bitsPerSample / 8));
        
        for (int i = 0; i < samplesPerChannel; i++) {
            if (bitsPerSample == 16) {
                short left = reader.ReadInt16();
                short right = channels == 2 ? reader.ReadInt16() : left;
                
                frames.Add(new AudioFrame(Int16ToFloat(left), Int16ToFloat(right)));
            } else {
                throw new NotSupportedException($"Unsupported bits per sample: {bitsPerSample}");
            }
        }
        
        return frames;
    }
    
    /// <summary>
    /// Convert float audio sample (-1.0 to 1.0) to int16.
    /// </summary>
    private static short FloatToInt16(float sample) {
        // Clamp to valid range
        float clamped = Math.Clamp(sample, -1.0f, 1.0f);
        
        // Convert to int16 range
        return (short)(clamped * short.MaxValue);
    }
    
    /// <summary>
    /// Convert int16 audio sample to float (-1.0 to 1.0).
    /// </summary>
    private static float Int16ToFloat(short sample) {
        return sample / (float)short.MaxValue;
    }
}
