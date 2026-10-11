namespace Spice86.Tests.Bios;

using FluentAssertions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.InterruptHandlers.VGA;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Data;
using Spice86.Core.Emulator.Memory;
using Spice86.Tests.AsmFixtures;

using Xunit;

public sealed class VgaRomTests {
    [Fact]
    public void MappedMemoryWritesDoNotPersistInRom() {
        // Arrange
        using AsmFixtureRun run = AsmFixtureRunner.RunEmulated(AsmFixtureCatalog.RomWrite(), JitMode.InterpretedOnly);
        IMemory memory = run.Machine.Memory;
        VgaRom rom = new(NullLogger.Instance);
        const uint baseAddress = VgaRom.Segment << 4;
        byte[] original = memory.ReadRam(rom.Size, baseAddress);

        // Act
        memory.WriteRam([0xC0, 0xC1], 0xC1190);
        memory.LoadData(0xC1190, new byte[] { 0xC2, 0xC3 });
        memory.SneakilyWrite(0xC1190, 0xC4);
        memory.UInt16[0xB800, 0x9190] = 0xA55A;

        // Assert
        memory.ReadRam(rom.Size, baseAddress).Should().Equal(original);
    }

    [Fact]
    public void WritesLeaveEveryRomByteUnchanged() {
        // Arrange
        VgaRom rom = new(NullLogger.Instance);
        const uint baseAddress = VgaRom.Segment << 4;
        byte[] original = rom.GetSlice((int)baseAddress, (int)rom.Size).ToArray();

        // Act
        for (uint offset = 0; offset < rom.Size; offset++) {
            rom.Write(baseAddress + offset, (byte)~original[offset]);
        }

        // Assert
        rom.GetSlice((int)baseAddress, (int)rom.Size).Should().Equal(original);
    }

    [Fact]
    public void ConstructionPreservesFontContentsAndAddresses() {
        // Arrange
        VgaRom rom = new(NullLogger.Instance);
        const int baseAddress = VgaRom.Segment << 4;

        // Act
        byte[] contents = rom.GetSlice(baseAddress, (int)rom.Size).ToArray();

        // Assert
        rom.VgaFont8Address.Offset.Should().Be(64);
        rom.VgaFont8Address2.Offset.Should().Be((ushort)(64 + Fonts.VgaFont8.Length / 2));
        rom.VgaFont14Address.Offset.Should().Be((ushort)(64 + Fonts.VgaFont8.Length));
        rom.VgaFont16Address.Offset.Should().Be((ushort)(64 + Fonts.VgaFont8.Length + Fonts.VgaFont14.Length));
        contents.Take(64).Should().OnlyContain(value => value == 0);
        contents.Skip(64).Should().Equal(Fonts.VgaFont8.Concat(Fonts.VgaFont14).Concat(Fonts.VgaFont16));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IgnoredWriteDiagnosticIsGuardedByDebugLevel(bool debugEnabled) {
        // Arrange
        ILogger logger = Substitute.For<ILogger>();
        logger.IsEnabled(LogLevel.Debug).Returns(debugEnabled);
        VgaRom rom = new(logger);
        const uint address = 0xC1190;
        byte original = rom.Read(address);

        // Act
        rom.Write(address, 0xC0);

        // Assert
        rom.Read(address).Should().Be(original);
        logger.Received(1).IsEnabled(LogLevel.Debug);
        string[] messages = logger.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log))
            .Select(call => call.GetArguments()[2]?.ToString() ?? "")
            .ToArray();
        if (debugEnabled) {
            messages.Should().Equal("Ignoring write of C0 to VGA ROM at 0C1190");
        } else {
            messages.Should().BeEmpty();
        }
    }
}
