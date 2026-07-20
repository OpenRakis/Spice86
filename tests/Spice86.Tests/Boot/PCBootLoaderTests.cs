namespace Spice86.Tests.Boot;

using FluentAssertions;
using Microsoft.Extensions.Logging;

using NSubstitute;

using Spice86.Core.Emulator.Boot;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Tests.Dos.FileSystem;
using Spice86.Shared.Interfaces;

using Xunit;

public class PCBootLoaderTests {
    [Fact]
    public void TryBootFromRealFloppyImage_LoadsSectorAt7C00() {
        // Arrange
        PCBootLoaderContext context = CreateContext();
        byte[] image = CreateRealBootFloppyImage();

        // Act
        bool booted = context.Loader.TryBootFromFloppyImage(image, 0, "test.img");

        // Assert
        booted.Should().BeTrue();
        context.Memory.UInt8[0x7C00 + 0x40].Should().Be(0xDE);
        context.Memory.UInt8[0x7C00 + 0x41].Should().Be(0xAD);
        context.Memory.UInt8[0x7C00 + 0x42].Should().Be(0xBE);
        context.Memory.UInt8[0x7C00 + 0x43].Should().Be(0xEF);
        context.Memory.UInt8[0x7C00 + 510].Should().Be(0x55);
        context.Memory.UInt8[0x7C00 + 511].Should().Be(0xAA);
    }

    [Fact]
    public void TryBootFromRealFloppyImage_SetsCpuStateForBiosBootProtocol() {
        // Arrange
        PCBootLoaderContext context = CreateContext();
        byte[] image = CreateRealBootFloppyImage();

        // Act
        context.Loader.TryBootFromFloppyImage(image, 0, "test.img");

        // Assert
        context.State.CS.Should().Be(0);
        context.State.IP.Should().Be(0x7C00);
        context.State.DS.Should().Be(0);
        context.State.ES.Should().Be(0);
        context.State.SS.Should().Be(0);
        context.State.SP.Should().Be(0x7C00);
        context.State.DL.Should().Be(0x00);
        context.State.AX.Should().Be(0);
        context.State.CX.Should().Be(1);
        context.State.BX.Should().Be(0x7C00);
        context.State.BP.Should().Be(0);
        context.State.SI.Should().Be(0);
        context.State.DI.Should().Be(0);
        context.State.InterruptFlag.Should().BeTrue();
    }

    [Fact]
    public void TryBootFromRealFloppyImage_DriveB_SetsDLToOne() {
        // Arrange
        PCBootLoaderContext context = CreateContext();
        byte[] image = CreateRealBootFloppyImage();

        // Act
        context.Loader.TryBootFromFloppyImage(image, 1, "test.img");

        // Assert
        context.State.DL.Should().Be(0x01);
    }

    [Fact]
    public void TryBootFromRealFloppyImage_MissingBootSignature_ReturnsFalse() {
        // Arrange
        PCBootLoaderContext context = CreateContext();
        byte[] image = CreateRealBootFloppyImage();
        image[510] = 0x00;
        image[511] = 0x00;

        // Act
        bool booted = context.Loader.TryBootFromFloppyImage(image, 0, "test.img");

        // Assert
        booted.Should().BeFalse();
    }

    [Fact]
    public void TryBootFromRealFloppyImage_PreservesRealFloppyBootSignature() {
        // Arrange
        PCBootLoaderContext context = CreateContext();
        byte[] image = CreateRealBootFloppyImage();

        // Act
        bool booted = context.Loader.TryBootFromFloppyImage(image, 0, "test.img");

        // Assert
        booted.Should().BeTrue();
        image[510].Should().Be(0x55);
        image[511].Should().Be(0xAA);
    }

    private static PCBootLoaderContext CreateContext() {
        IMemory memory = new Memory(new(), new Ram(0x200000), new A20Gate(), new RealModeMmu386(), false);
        State state = new(CpuModel.INTEL_80286);
        ILoggerService loggerService = Substitute.For<ILoggerService>();
        PCBootLoader loader = new(memory, state, loggerService);
        return new PCBootLoaderContext(loader, memory, state);
    }

    private static byte[] CreateRealBootFloppyImage() {
        byte[] image = new Fat12ImageBuilder()
            .WithFile("README.TXT", new byte[] { (byte)'S', (byte)'P', (byte)'I', (byte)'C', (byte)'E', (byte)'8', (byte)'6' })
            .Build();

        PatchBootSector(image);
        return image;
    }

    private static void PatchBootSector(byte[] image) {
        image[0x40] = 0xDE;
        image[0x41] = 0xAD;
        image[0x42] = 0xBE;
        image[0x43] = 0xEF;
    }

    private sealed record PCBootLoaderContext(PCBootLoader Loader, IMemory Memory, State State);
}
