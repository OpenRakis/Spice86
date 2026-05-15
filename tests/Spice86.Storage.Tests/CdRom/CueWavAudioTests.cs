namespace Spice86.Storage.Tests.CdRom;

using FluentAssertions;

using Spice86.Shared.Emulator.Storage.CdRom;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Xunit;

/// <summary>
/// TDD tests for CUE sheet FILE-type dispatch and WAV audio file decoding
/// (Phase 4, atom 1). Verifies that CUE sheets referencing CDDA-compliant
/// WAV files can be loaded end-to-end and read as raw 2352-byte audio
/// sectors, without breaking the legacy BINARY pipeline.
/// </summary>
public sealed class CueWavAudioTests : IDisposable
{
    private const int RawSectorSize = 2352;
    private readonly string _tempDirectory;
    private readonly List<string> _temporaryArtifacts = new();

    public CueWavAudioTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "Spice86_CueWavAudioTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _temporaryArtifacts.Add(_tempDirectory);
    }

    public void Dispose()
    {
        foreach (string path in _temporaryArtifacts)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // best-effort cleanup
            }
        }
    }

    [Fact]
    public void CueSheetParser_FileWaveDirective_ExtractsWaveFileType()
    {
        // Arrange
        string wavPath = WriteCddaWav("track01.wav", samplesPerChannel: 588);
        string cuePath = WriteCueSheet("disc.cue",
            $"FILE \"{Path.GetFileName(wavPath)}\" WAVE",
            "  TRACK 01 AUDIO",
            "    INDEX 01 00:02:00");

        // Act
        CueSheet sheet = new CueSheetParser().Parse(cuePath);

        // Assert
        sheet.Entries.Should().HaveCount(1);
        sheet.Entries[0].FileType.Should().Be(CueFileType.Wave);
        sheet.Entries[0].TrackMode.Should().Be("AUDIO");
    }

    [Fact]
    public void CueSheetParser_FileBinaryDirective_DefaultsToBinaryWhenTypeOmitted()
    {
        // Arrange
        string binPath = Path.Combine(_tempDirectory, "data.bin");
        File.WriteAllBytes(binPath, new byte[RawSectorSize]);
        string cuePath = WriteCueSheet("legacy.cue",
            "FILE \"data.bin\" BINARY",
            "  TRACK 01 MODE1/2352",
            "    INDEX 01 00:00:00");

        // Act
        CueSheet sheet = new CueSheetParser().Parse(cuePath);

        // Assert
        sheet.Entries.Should().HaveCount(1);
        sheet.Entries[0].FileType.Should().Be(CueFileType.Binary);
    }

    [Fact]
    public void WavAudioFile_OpenCddaCompliantWav_ExposesPcmDataOffsetAndLength()
    {
        // Arrange
        string wavPath = WriteCddaWav("audio.wav", samplesPerChannel: 1176); // 2 sectors worth.

        // Act
        WavAudioFile wav = new(wavPath);

        // Assert
        wav.SampleRate.Should().Be(44100);
        wav.ChannelCount.Should().Be(2);
        wav.BitsPerSample.Should().Be(16);
        wav.PcmDataLength.Should().Be(1176 * 2 * 2);
        wav.PcmDataOffset.Should().BeGreaterThan(0);
    }

    [Fact]
    public void WavAudioFile_OpenNonCddaWav_ThrowsInvalidDataException()
    {
        // Arrange: 22050 Hz mono is rejected for the CDDA pipeline.
        string wavPath = WriteWavWithFormat("nonCdda.wav", sampleRate: 22050, channels: 1, bitsPerSample: 16, samplesPerChannel: 588);

        // Act
        Action act = () => _ = new WavAudioFile(wavPath);

        // Assert
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void CueBinImage_FileWaveAudio_ReadsPcmBytesAsRawAudioSector()
    {
        // Arrange: WAV containing two sectors of distinct PCM payloads.
        byte[] payloadSector0 = MakeDistinctivePcmSector(seed: 0x12);
        byte[] payloadSector1 = MakeDistinctivePcmSector(seed: 0x77);
        byte[] pcmAllSectors = new byte[RawSectorSize * 2];
        Buffer.BlockCopy(payloadSector0, 0, pcmAllSectors, 0, RawSectorSize);
        Buffer.BlockCopy(payloadSector1, 0, pcmAllSectors, RawSectorSize, RawSectorSize);
        string wavPath = WriteWavRaw("track.wav", pcmAllSectors, sampleRate: 44100, channels: 2, bitsPerSample: 16);
        string cuePath = WriteCueSheet("audio.cue",
            $"FILE \"{Path.GetFileName(wavPath)}\" WAVE",
            "  TRACK 01 AUDIO",
            "    INDEX 01 00:02:00");

        using CueBinImage image = new(cuePath);

        // Act
        byte[] readSector0 = new byte[RawSectorSize];
        byte[] readSector1 = new byte[RawSectorSize];
        image.Read(0, readSector0, CdSectorMode.AudioRaw2352);
        image.Read(1, readSector1, CdSectorMode.AudioRaw2352);

        // Assert
        image.Tracks.Should().HaveCount(1);
        image.Tracks[0].IsAudio.Should().BeTrue();
        readSector0.Should().BeEquivalentTo(payloadSector0);
        readSector1.Should().BeEquivalentTo(payloadSector1);
    }

    [Fact]
    public void CueBinImage_LegacyFileBinary_StillReadsRawBinSectors()
    {
        // Arrange (regression for existing BINARY pipeline).
        byte[] payload = MakeDistinctivePcmSector(seed: 0x5A);
        string binPath = Path.Combine(_tempDirectory, "legacy.bin");
        File.WriteAllBytes(binPath, payload);
        string cuePath = WriteCueSheet("legacy.cue",
            "FILE \"legacy.bin\" BINARY",
            "  TRACK 01 AUDIO",
            "    INDEX 01 00:00:00");

        using CueBinImage image = new(cuePath);

        // Act
        byte[] readSector = new byte[RawSectorSize];
        image.Read(0, readSector, CdSectorMode.AudioRaw2352);

        // Assert
        readSector.Should().BeEquivalentTo(payload);
    }

    private string WriteCueSheet(string name, params string[] lines)
    {
        string path = Path.Combine(_tempDirectory, name);
        File.WriteAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
        return path;
    }

    private string WriteCddaWav(string name, int samplesPerChannel)
    {
        byte[] pcm = new byte[samplesPerChannel * 2 * 2];
        for (int i = 0; i < pcm.Length; i++)
        {
            pcm[i] = (byte)(i & 0xFF);
        }
        return WriteWavRaw(name, pcm, sampleRate: 44100, channels: 2, bitsPerSample: 16);
    }

    private string WriteWavWithFormat(string name, int sampleRate, int channels, int bitsPerSample, int samplesPerChannel)
    {
        byte[] pcm = new byte[samplesPerChannel * channels * (bitsPerSample / 8)];
        for (int i = 0; i < pcm.Length; i++)
        {
            pcm[i] = (byte)(i & 0xFF);
        }
        return WriteWavRaw(name, pcm, sampleRate, channels, bitsPerSample);
    }

    private string WriteWavRaw(string name, byte[] pcm, int sampleRate, int channels, int bitsPerSample)
    {
        string path = Path.Combine(_tempDirectory, name);
        int byteRate = sampleRate * channels * (bitsPerSample / 8);
        ushort blockAlign = (ushort)(channels * (bitsPerSample / 8));
        byte[] header = new byte[44];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(header, 0);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), 36 + pcm.Length);
        Encoding.ASCII.GetBytes("WAVE").CopyTo(header, 8);
        Encoding.ASCII.GetBytes("fmt ").CopyTo(header, 12);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16, 4), 16); // subchunk1 size for PCM
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(20, 2), 1); // PCM
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(22, 2), (ushort)channels);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(24, 4), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(28, 4), byteRate);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(32, 2), blockAlign);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(34, 2), (ushort)bitsPerSample);
        Encoding.ASCII.GetBytes("data").CopyTo(header, 36);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(40, 4), pcm.Length);

        using (FileStream stream = File.Create(path))
        {
            stream.Write(header);
            stream.Write(pcm);
        }
        return path;
    }

    private static byte[] MakeDistinctivePcmSector(byte seed)
    {
        byte[] sector = new byte[RawSectorSize];
        for (int i = 0; i < sector.Length; i++)
        {
            sector[i] = (byte)((i + seed) & 0xFF);
        }
        return sector;
    }
}
