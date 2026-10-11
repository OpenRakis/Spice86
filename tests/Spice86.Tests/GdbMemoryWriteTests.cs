namespace Spice86.Tests;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Spice86.Core.Emulator.Gdb;
using Spice86.Core.Emulator.InterruptHandlers.VGA;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;

using Xunit;

public sealed class GdbMemoryWriteTests {
    [Fact]
    public void WriteMemory_ReportsSuccessOnlyForPersistedRamWrites() {
        // Arrange
        IMemory memory = CreateMemory();
        using GdbIo io = new(0, NullLogger.Instance);
        GdbCommandMemoryHandler handler = new(memory, io, NullLogger.Instance);

        // Act
        string response = handler.WriteMemory("500,2:C0FF");

        // Assert
        response.Should().Be(io.GenerateResponse("OK"));
        memory.ReadRam(2, 0x500).Should().Equal(0xC0, 0xFF);
    }

    [Fact]
    public void WriteMemory_ReportsAnErrorWhenRomIgnoresTheWrite() {
        // Arrange
        IMemory memory = CreateMemory();
        byte[] original = memory.ReadRam(2, 0xC1190);
        string data = Convert.ToHexString(original.Select(value => (byte)~value).ToArray());
        using GdbIo io = new(0, NullLogger.Instance);
        GdbCommandMemoryHandler handler = new(memory, io, NullLogger.Instance);

        // Act
        string response = handler.WriteMemory($"C1190,2:{data}");

        // Assert
        response.Should().Be(io.GenerateResponse("E03"));
        memory.ReadRam(2, 0xC1190).Should().Equal(original);
    }

    [Fact]
    public void WriteMemory_VerifiesA20DisabledHighMemoryWritesAtTranslatedAddresses() {
        // Arrange
        Memory memory = CreateMemoryWithA20Disabled();
        using GdbIo io = new(0, NullLogger.Instance);
        GdbCommandMemoryHandler handler = new(memory, io, NullLogger.Instance);

        // Act
        string response = handler.WriteMemory("100000,2:C0FF");

        // Assert
        response.Should().Be(io.GenerateResponse("OK"));
        memory.ReadRam(2, 0).Should().Equal(0xC0, 0xFF);
    }

    [Fact]
    public void WriteMemory_VerifiesA20DisabledWritesAcrossTheOneMegabyteBoundary() {
        // Arrange
        Memory memory = CreateMemoryWithA20Disabled();
        using GdbIo io = new(0, NullLogger.Instance);
        GdbCommandMemoryHandler handler = new(memory, io, NullLogger.Instance);

        // Act
        string response = handler.WriteMemory("FFFFF,2:C0FF");

        // Assert
        response.Should().Be(io.GenerateResponse("OK"));
        memory.ReadRam(1, 0xFFFFF).Should().Equal(0xC0);
        memory.ReadRam(1, 0).Should().Equal(0xFF);
    }

    [Theory]
    [InlineData("500,3:C0FF", "E01")]
    [InlineData("FFFFF,2:C0FF", "E02")]
    [InlineData("FFFFFFFF,2:C0FF", "E02")]
    [InlineData("500:C0FF", "E01")]
    public void WriteMemory_RejectsInvalidLengthsAndRanges(string command, string error) {
        // Arrange
        IMemory memory = CreateMemory();
        using GdbIo io = new(0, NullLogger.Instance);
        GdbCommandMemoryHandler handler = new(memory, io, NullLogger.Instance);

        // Act
        string response = handler.WriteMemory(command);

        // Assert
        response.Should().Be(io.GenerateResponse(error));
    }

    [Fact]
    public void WriteMemory_AllowsAnEmptyWriteWithoutReadingTheEntireBus() {
        // Arrange
        IMemory memory = CreateMemory();
        using GdbIo io = new(0, NullLogger.Instance);
        GdbCommandMemoryHandler handler = new(memory, io, NullLogger.Instance);

        // Act
        string response = handler.WriteMemory("500,0:");

        // Assert
        response.Should().Be(io.GenerateResponse("OK"));
    }

    private static IMemory CreateMemory() {
        Memory memory = new(new(), new Ram(0x100000), new A20Gate(), new RealModeMmu386(), false);
        VgaRom rom = new(NullLogger.Instance);
        memory.RegisterMapping(VgaRom.Segment << 4, rom.Size, rom);
        return memory;
    }

    private static Memory CreateMemoryWithA20Disabled() {
        return new(new(), new Ram(0x110000), new A20Gate(false), new RealModeMmu386(), false);
    }
}
