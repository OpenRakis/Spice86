namespace Spice86.Tests.Dos;

using System;
using System.IO;
using System.Text;

using FluentAssertions;

using Spice86.Shared.Emulator.Storage.CdRom;
using Spice86.Tests.Utility;

using Xunit;

public class CdStorageParsingSafetyTests {
    [Fact]
    public void MemoryDataSource_Read_NegativeOffset_ReturnsZero() {
        // Arrange
        MemoryDataSource source = new([1, 2, 3, 4]);
        Span<byte> destination = stackalloc byte[2];

        // Act
        int bytesRead = source.Read(-1, destination);

        // Assert
        bytesRead.Should().Be(0);
    }

    [Fact]
    public void MemoryDataSource_Read_OffsetGreaterThanIntMaxValue_ReturnsZero() {
        // Arrange
        MemoryDataSource source = new([1, 2, 3, 4]);
        Span<byte> destination = stackalloc byte[2];

        // Act
        int bytesRead = source.Read((long)int.MaxValue + 1, destination);

        // Assert
        bytesRead.Should().Be(0);
    }

    [Fact]
    public void CueSheetParser_Parse_MalformedFileDirective_ThrowsInvalidDataException() {
        // Arrange
        using TempFile tempFile = new("cue_parse_guard");
        string cuePath = tempFile.CreateTextFile("broken.cue",
            "FILE \"BROKEN.BIN\"\r\n" +
            "  TRACK 01 MODE1/2048\r\n" +
            "  INDEX 01 00:00:00\r\n");
        CueSheetParser parser = new();

        // Act
        Action act = () => parser.Parse(cuePath);

        // Assert
        act.Should().Throw<InvalidDataException>()
            .WithMessage("*Malformed FILE directive*");
    }

    [Fact]
    public void WavAudioFile_Ctor_NegativeChunkSize_ThrowsInvalidDataException() {
        // Arrange
        using TempFile tempFile = new("wav_negative_chunk");
        string wavPath = tempFile.CreateFile("broken.wav", CreateWavWithNegativeFmtChunkSize());

        // Act
        Action act = () => new WavAudioFile(wavPath);

        // Assert
        act.Should().Throw<InvalidDataException>()
            .WithMessage("*negative size*");
    }

    [Fact]
    public void WavAudioFile_Ctor_ChunkPastEnd_ThrowsInvalidDataException() {
        // Arrange
        using TempFile tempFile = new("wav_chunk_past_end");
        string wavPath = tempFile.CreateFile("broken.wav", CreateWavWithFmtChunkExceedingFile());

        // Act
        Action act = () => new WavAudioFile(wavPath);

        // Assert
        act.Should().Throw<InvalidDataException>()
            .WithMessage("*extends beyond end of file*");
    }

    private static byte[] CreateWavWithNegativeFmtChunkSize() {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(12);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(-1);

        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] CreateWavWithFmtChunkExceedingFile() {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(28);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(1000);
        writer.Write(new byte[16]);

        writer.Flush();
        return stream.ToArray();
    }
}