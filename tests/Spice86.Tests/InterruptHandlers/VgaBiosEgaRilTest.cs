namespace Spice86.Tests.InterruptHandlers;

using Spice86.Core.Emulator.InterruptHandlers.VGA;
using Spice86.Core.Emulator.IOPorts;

using FluentAssertions;
using Xunit;

/// <summary>
/// Behavioral tests for the INT 10h AH=F1h EGA Raster Image Library write-register service.
/// </summary>
public class VgaBiosEgaRilTest : IDisposable {
    private readonly ServiceContractMachine _machine = new(enableEms: false, enableXms: false);

    public void Dispose() {
        _machine.Dispose();
    }

    [Fact]
    public void Int10F1_GraphicsControllerRegister_WritesValue() {
        // Arrange
        VgaBios vgaBios = Vga();
        _machine.Spice86.Machine.CpuState.AH = 0xF1;
        _machine.Spice86.Machine.CpuState.DX = 0x10;
        _machine.Spice86.Machine.CpuState.BL = 0x03;
        _machine.Spice86.Machine.CpuState.BH = 0x05;

        // Act
        vgaBios.Run();

        // Assert
        IOPortDispatcher io = _machine.Spice86.Machine.IoPortDispatcher;
        io.WriteByte(0x3CE, 0x03);
        io.ReadByte(0x3CF).Should().Be(0x05);
    }

    [Fact]
    public void Int10F1_SequencerRegister_WritesValue() {
        // Arrange
        VgaBios vgaBios = Vga();
        _machine.Spice86.Machine.CpuState.AH = 0xF1;
        _machine.Spice86.Machine.CpuState.DX = 0x08;
        _machine.Spice86.Machine.CpuState.BL = 0x04;
        _machine.Spice86.Machine.CpuState.BH = 0x03;

        // Act
        vgaBios.Run();

        // Assert
        IOPortDispatcher io = _machine.Spice86.Machine.IoPortDispatcher;
        io.WriteByte(0x3C4, 0x04);
        io.ReadByte(0x3C5).Should().Be(0x03);
    }

    [Fact]
    public void Int10F1_CrtControllerRegister_WritesValue() {
        // Arrange
        VgaBios vgaBios = Vga();
        _machine.Spice86.Machine.CpuState.AH = 0xF1;
        _machine.Spice86.Machine.CpuState.DX = 0x00;
        _machine.Spice86.Machine.CpuState.BL = 0x12;
        _machine.Spice86.Machine.CpuState.BH = 0x40;

        // Act
        vgaBios.Run();

        // Assert
        IOPortDispatcher io = _machine.Spice86.Machine.IoPortDispatcher;
        int crtBase = (io.ReadByte(0x3CC) & 1) != 0 ? 0x3D4 : 0x3B4;
        io.WriteByte((ushort)crtBase, 0x12);
        io.ReadByte((ushort)(crtBase + 1)).Should().Be(0x40);
    }

    [Fact]
    public void Int10F1_AttributeControllerRegister_WritesValue() {
        // Arrange
        VgaBios vgaBios = Vga();
        _machine.Spice86.Machine.CpuState.AH = 0xF1;
        _machine.Spice86.Machine.CpuState.DX = 0x18;
        _machine.Spice86.Machine.CpuState.BL = 0x02;
        _machine.Spice86.Machine.CpuState.BH = 0x11;

        // Act
        vgaBios.Run();

        // Assert
        IOPortDispatcher io = _machine.Spice86.Machine.IoPortDispatcher;
        io.ReadByte(0x3BA);
        io.WriteByte(0x3C0, 0x02);
        io.ReadByte(0x3C1).Should().Be(0x11);
    }

    [Fact]
    public void Int10F1_UnknownPortSelection_DoesNotThrow() {
        // Arrange
        VgaBios vgaBios = Vga();
        _machine.Spice86.Machine.CpuState.AH = 0xF1;
        _machine.Spice86.Machine.CpuState.DX = 0xFF;
        _machine.Spice86.Machine.CpuState.BL = 0x01;
        _machine.Spice86.Machine.CpuState.BH = 0x02;

        // Act / Assert
        Action run = () => vgaBios.Run();
        run.Should().NotThrow();
    }

    private VgaBios Vga() {
        return (VgaBios)_machine.Spice86.Machine.InstalledInterruptHandlers.Handlers[0x10];
    }
}
