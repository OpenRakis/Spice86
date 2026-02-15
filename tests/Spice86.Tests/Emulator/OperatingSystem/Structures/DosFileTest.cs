namespace Spice86.Tests.Emulator.OperatingSystem.Structures;

using FluentAssertions;

using Spice86.Core.Emulator.OperatingSystem.Structures;

using Xunit;

public class DosFileTests {
    [Fact]
    public void DosFile_DelegatesStreamCapabilitiesAndPosition() {
        byte[] data = new byte[256];
        using MemoryStream memoryStream = new(data, true);
        using DosFile dosFile = new("TEST.DAT", 3, memoryStream);

        dosFile.CanRead.Should().Be(memoryStream.CanRead);
        dosFile.CanSeek.Should().Be(memoryStream.CanSeek);
        dosFile.CanWrite.Should().Be(memoryStream.CanWrite);
        dosFile.Length.Should().Be(memoryStream.Length);

        dosFile.Seek(128, SeekOrigin.Begin);
        dosFile.Position.Should().Be(128);
        memoryStream.Position.Should().Be(128);

        dosFile.Position = 64;
        dosFile.Position.Should().Be(64);
        memoryStream.Position.Should().Be(64);

        byte[] buffer = new byte[16];
        int read = dosFile.Read(buffer, 0, buffer.Length);
        read.Should().Be(buffer.Length);
        dosFile.Position.Should().Be(80);
        memoryStream.Position.Should().Be(80);

        dosFile.Seek(-20, SeekOrigin.Current);
        dosFile.Position.Should().Be(60);
        memoryStream.Position.Should().Be(60);
    }
}



