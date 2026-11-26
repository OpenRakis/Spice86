namespace Spice86.Tests.Video;

using FluentAssertions;

using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;

using System.Text.Json;

using Xunit;

/// <summary>
/// Checks <see cref="VgaCard.CaptureState" /> and <see cref="VgaCard.RestoreState" /> on a whole machine: capture,
/// change the card through its ports and video memory, restore, and compare what the ports and the video memory
/// return with what they returned at capture.
/// </summary>
public sealed class VgaStateTests {
    private const ushort AttributeAddressPort = 0x3C0;
    private const ushort AttributeReadPort = 0x3C1;
    private const ushort SequencerAddressPort = 0x3C4;
    private const ushort SequencerDataPort = 0x3C5;
    private const ushort DacPixelMaskPort = 0x3C6;
    private const ushort DacReadIndexPort = 0x3C7;
    private const ushort DacWriteIndexPort = 0x3C8;
    private const ushort DacDataPort = 0x3C9;
    private const ushort GraphicsAddressPort = 0x3CE;
    private const ushort GraphicsDataPort = 0x3CF;
    private const ushort CrtAddressPort = 0x3D4;
    private const ushort CrtDataPort = 0x3D5;
    private const ushort InputStatus1Port = 0x3DA;

    private const byte SequencerMapMask = 2;
    private const byte SequencerMemoryMode = 4;
    private const byte GraphicsEnableSetReset = 1;
    private const byte GraphicsDataRotate = 3;
    private const byte GraphicsReadMapSelect = 4;
    private const byte GraphicsMode = 5;
    private const byte GraphicsMiscellaneous = 6;
    private const byte GraphicsBitMask = 8;
    private const byte CrtHorizontalTotal = 0;
    private const byte CrtCursorLocationLow = 0x0F;
    private const byte CrtVerticalRetraceEnd = 0x11;
    private const byte CrtWriteProtect = 0x80;
    private const byte AttributePaletteEntry5 = 5;
    private const byte AttributeOverscanColor = 0x11;

    private const byte SequentialPlanarMemory = 0x06;
    private const byte GraphicsAtA0000Size64K = 0x05;
    private const byte WriteMode0ReadMode0 = 0x00;
    private const byte WriteMode1 = 0x01;
    private const uint VideoMemoryStart = 0xA0000;

    [Fact]
    public void RestoreGivesBackTheRegistersAndTheDacThroughThePorts() {
        // Arrange
        using Spice86Creator creator = new("externalint");
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        Machine machine = dependencyInjection.Machine;
        IOPortDispatcher ports = machine.IoPortDispatcher;
        ProgramRegisters(ports, 0x0A, 0x41, 0x17, 0x2A);
        VgaState captured = machine.VgaCard.CaptureState();
        RegisterReads before = ReadRegisters(ports);

        // Act
        ProgramRegisters(ports, 0x05, 0x99, 0x03, 0x3F);
        RegisterReads changed = ReadRegisters(ports);
        machine.VgaCard.RestoreState(captured);

        // Assert
        changed.Should().NotBe(before);
        ReadRegisters(ports).Should().Be(before);
    }

    [Fact]
    public void RestoreRebuildsTheColoursTheRendererDrawsWithAndRedrawsTheFrame() {
        // Arrange
        using Spice86Creator creator = new("externalint");
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        Machine machine = dependencyInjection.Machine;
        IOPortDispatcher ports = machine.IoPortDispatcher;
        IVideoState video = machine.VgaRegisters;
        VideoMemory videoMemory = ((Renderer)machine.VgaRenderer).VideoMemory;
        ProgramRegisters(ports, 0x0A, 0x41, 0x17, 0x10);
        VgaState captured = machine.VgaCard.CaptureState();
        uint[] paletteMapBefore = video.DacRegisters.PaletteMap.ToArray();
        uint[] attributeMapBefore = video.DacRegisters.AttributeMap.ToArray();

        // Act
        ProgramRegisters(ports, 0x05, 0x99, 0x03, 0x3F);
        video.IsRenderingDirty = false;
        videoMemory.ResetChanged();
        machine.VgaCard.RestoreState(captured);

        // Assert: attribute entry 5 selects DAC entry 10h, which both programs change
        video.DacRegisters.PaletteMap.Should().Equal(paletteMapBefore);
        video.DacRegisters.AttributeMap.Should().Equal(attributeMapBefore);
        video.IsRenderingDirty.Should().BeTrue();
        videoMemory.HasChanged.Should().BeTrue();
    }

    [Fact]
    public void RestoreGivesBackTheVideoMemoryPlanes() {
        // Arrange
        using Spice86Creator creator = new("externalint");
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        Machine machine = dependencyInjection.Machine;
        IOPortDispatcher ports = machine.IoPortDispatcher;
        SelectPlanarMode(ports);
        WritePlanes(machine, VideoMemoryStart + 0x100, 0x11, 0x22, 0x33, 0x44);
        VgaState captured = machine.VgaCard.CaptureState();

        // Act
        WritePlanes(machine, VideoMemoryStart + 0x100, 0x55, 0x66, 0x77, 0x88);
        machine.VgaCard.RestoreState(captured);

        // Assert
        SelectPlanarMode(ports);
        ReadPlanes(machine, VideoMemoryStart + 0x100).Should().Equal(0x11, 0x22, 0x33, 0x44);
    }

    [Fact]
    public void RestoreGivesBackTheLatchesThatWriteMode1Copies() {
        // Arrange
        using Spice86Creator creator = new("externalint");
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        Machine machine = dependencyInjection.Machine;
        IOPortDispatcher ports = machine.IoPortDispatcher;
        SelectPlanarMode(ports);
        WritePlanes(machine, VideoMemoryStart, 0x11, 0x22, 0x33, 0x44);
        WritePlanes(machine, VideoMemoryStart + 1, 0x55, 0x66, 0x77, 0x88);
        _ = machine.Memory.UInt8[VideoMemoryStart];
        VgaState captured = machine.VgaCard.CaptureState();

        // Act: a read loads other latches, then the restore puts the captured ones back
        _ = machine.Memory.UInt8[VideoMemoryStart + 1];
        machine.VgaCard.RestoreState(captured);
        WriteGraphics(ports, GraphicsMode, WriteMode1);
        machine.Memory.UInt8[VideoMemoryStart + 2] = 0;

        // Assert
        WriteGraphics(ports, GraphicsMode, WriteMode0ReadMode0);
        ReadPlanes(machine, VideoMemoryStart + 2).Should().Equal(0x11, 0x22, 0x33, 0x44);
    }

    [Fact]
    public void RestoredDacReadContinuesInsideTheEntry() {
        // Arrange
        using Spice86Creator creator = new("externalint");
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        Machine machine = dependencyInjection.Machine;
        IOPortDispatcher ports = machine.IoPortDispatcher;
        WriteDacEntry(ports, 0x20, 0x01, 0x02, 0x03);
        ports.WriteByte(DacReadIndexPort, 0x20);
        _ = ports.ReadByte(DacDataPort);
        VgaState captured = machine.VgaCard.CaptureState();

        // Act
        WriteDacEntry(ports, 0x20, 0x31, 0x32, 0x33);
        ports.WriteByte(DacReadIndexPort, 0x40);
        machine.VgaCard.RestoreState(captured);

        // Assert: the next reads are green and blue of entry 20h
        ports.ReadByte(DacDataPort).Should().Be(0x02);
        ports.ReadByte(DacDataPort).Should().Be(0x03);
    }

    [Fact]
    public void RestoredAttributeTogglePointsAtTheDataWrite() {
        // Arrange
        using Spice86Creator creator = new("externalint");
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        Machine machine = dependencyInjection.Machine;
        IOPortDispatcher ports = machine.IoPortDispatcher;
        _ = ports.ReadByte(InputStatus1Port);
        ports.WriteByte(AttributeAddressPort, AttributeOverscanColor);
        VgaState captured = machine.VgaCard.CaptureState();

        // Act: reading input status 1 sets the toggle back to the address write
        _ = ports.ReadByte(InputStatus1Port);
        machine.VgaCard.RestoreState(captured);
        ports.WriteByte(AttributeAddressPort, 0x2C);

        // Assert
        ports.ReadByte(AttributeAddressPort).Should().Be(AttributeOverscanColor);
        ports.ReadByte(AttributeReadPort).Should().Be(0x2C);
    }

    [Fact]
    public void RestoreWritesProtectedCrtRegistersAndPutsTheProtectionBack() {
        // Arrange
        using Spice86Creator creator = new("externalint");
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        Machine machine = dependencyInjection.Machine;
        IOPortDispatcher ports = machine.IoPortDispatcher;
        WriteCrt(ports, CrtVerticalRetraceEnd, 0);
        WriteCrt(ports, CrtHorizontalTotal, 0x5F);
        WriteCrt(ports, CrtVerticalRetraceEnd, CrtWriteProtect);
        VgaState captured = machine.VgaCard.CaptureState();

        // Act
        WriteCrt(ports, CrtVerticalRetraceEnd, 0);
        WriteCrt(ports, CrtHorizontalTotal, 0x2D);
        machine.VgaCard.RestoreState(captured);
        WriteCrt(ports, CrtHorizontalTotal, 0x2D);

        // Assert: the restored value is back, and the restored protection ignores the last write
        ReadCrt(ports, CrtHorizontalTotal).Should().Be(0x5F);
        ReadCrt(ports, CrtVerticalRetraceEnd).Should().Be(CrtWriteProtect);
    }

    [Fact]
    public void OneStateRestoresTheSameWayTwice() {
        // Arrange
        using Spice86Creator creator = new("externalint");
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        Machine machine = dependencyInjection.Machine;
        IOPortDispatcher ports = machine.IoPortDispatcher;
        SelectPlanarMode(ports);
        WritePlanes(machine, VideoMemoryStart, 0x11, 0x22, 0x33, 0x44);
        ProgramRegisters(ports, 0x0A, 0x41, 0x17, 0x2A);
        VgaState captured = machine.VgaCard.CaptureState();
        RegisterReads before = ReadRegisters(ports);

        // Act
        ProgramRegisters(ports, 0x05, 0x99, 0x03, 0x3F);
        machine.VgaCard.RestoreState(captured);
        SelectPlanarMode(ports);
        WritePlanes(machine, VideoMemoryStart, 0x55, 0x66, 0x77, 0x88);
        ProgramRegisters(ports, 0x05, 0x99, 0x03, 0x3F);
        machine.VgaCard.RestoreState(captured);

        // Assert
        ReadRegisters(ports).Should().Be(before);
        SelectPlanarMode(ports);
        ReadPlanes(machine, VideoMemoryStart).Should().Equal(0x11, 0x22, 0x33, 0x44);
    }

    [Fact]
    public void StateRestoresTheSameAfterAJsonRoundTrip() {
        // Arrange
        using Spice86Creator creator = new("externalint");
        using Spice86DependencyInjection dependencyInjection = creator.Create();
        Machine machine = dependencyInjection.Machine;
        IOPortDispatcher ports = machine.IoPortDispatcher;
        SelectPlanarMode(ports);
        WritePlanes(machine, VideoMemoryStart, 0x11, 0x22, 0x33, 0x44);
        ProgramRegisters(ports, 0x0A, 0x41, 0x17, 0x2A);
        MachineDeviceState captured = machine.CaptureDeviceState(MachineDevices.Vga);
        RegisterReads before = ReadRegisters(ports);
        string json = JsonSerializer.Serialize(captured);

        // Act
        ProgramRegisters(ports, 0x05, 0x99, 0x03, 0x3F);
        SelectPlanarMode(ports);
        WritePlanes(machine, VideoMemoryStart, 0x55, 0x66, 0x77, 0x88);
        MachineDeviceState? deserialized = JsonSerializer.Deserialize<MachineDeviceState>(json);
        machine.RestoreDeviceState(deserialized ?? throw new InvalidOperationException("No state was read."));

        // Assert
        JsonSerializer.Serialize(machine.CaptureDeviceState(MachineDevices.Vga)).Should().Be(json);
        ReadRegisters(ports).Should().Be(before);
        SelectPlanarMode(ports);
        ReadPlanes(machine, VideoMemoryStart).Should().Equal(0x11, 0x22, 0x33, 0x44);
    }

    /// <summary>
    /// Values a program reads back from the card, one per register file and the DAC.
    /// </summary>
    private readonly record struct RegisterReads(byte MapMask, byte CursorLocationLow, byte DataRotate,
        byte PaletteEntry5, byte DacPixelMask, byte DacRed, byte DacGreen, byte DacBlue);

    private static void ProgramRegisters(IOPortDispatcher ports, byte mapMask, byte cursorLocationLow,
        byte dataRotate, byte paletteEntry5) {
        WriteSequencer(ports, SequencerMapMask, mapMask);
        WriteCrt(ports, CrtCursorLocationLow, cursorLocationLow);
        WriteGraphics(ports, GraphicsDataRotate, dataRotate);
        _ = ports.ReadByte(InputStatus1Port);
        ports.WriteByte(AttributeAddressPort, AttributePaletteEntry5);
        ports.WriteByte(AttributeAddressPort, paletteEntry5);
        ports.WriteByte(DacPixelMaskPort, (byte)(mapMask | 0xF0));
        WriteDacEntry(ports, 0x10, mapMask, dataRotate, paletteEntry5);
    }

    private static RegisterReads ReadRegisters(IOPortDispatcher ports) {
        byte mapMask = ReadIndexed(ports, SequencerAddressPort, SequencerDataPort, SequencerMapMask);
        byte cursorLocationLow = ReadCrt(ports, CrtCursorLocationLow);
        byte dataRotate = ReadIndexed(ports, GraphicsAddressPort, GraphicsDataPort, GraphicsDataRotate);
        _ = ports.ReadByte(InputStatus1Port);
        ports.WriteByte(AttributeAddressPort, AttributePaletteEntry5);
        byte paletteEntry5 = ports.ReadByte(AttributeReadPort);
        _ = ports.ReadByte(InputStatus1Port);
        byte pixelMask = ports.ReadByte(DacPixelMaskPort);
        ports.WriteByte(DacReadIndexPort, 0x10);
        return new RegisterReads(mapMask, cursorLocationLow, dataRotate, paletteEntry5, pixelMask,
            ports.ReadByte(DacDataPort), ports.ReadByte(DacDataPort), ports.ReadByte(DacDataPort));
    }

    private static void SelectPlanarMode(IOPortDispatcher ports) {
        WriteSequencer(ports, SequencerMapMask, 0x0F);
        WriteSequencer(ports, SequencerMemoryMode, SequentialPlanarMemory);
        WriteGraphics(ports, GraphicsEnableSetReset, 0);
        WriteGraphics(ports, GraphicsDataRotate, 0);
        WriteGraphics(ports, GraphicsMode, WriteMode0ReadMode0);
        WriteGraphics(ports, GraphicsMiscellaneous, GraphicsAtA0000Size64K);
        WriteGraphics(ports, GraphicsBitMask, 0xFF);
    }

    private static void WritePlanes(Machine machine, uint address, params byte[] planeValues) {
        for (int plane = 0; plane < planeValues.Length; plane++) {
            WriteSequencer(machine.IoPortDispatcher, SequencerMapMask, (byte)(1 << plane));
            machine.Memory.UInt8[address] = planeValues[plane];
        }
        WriteSequencer(machine.IoPortDispatcher, SequencerMapMask, 0x0F);
    }

    private static byte[] ReadPlanes(Machine machine, uint address) {
        byte[] values = new byte[4];
        for (int plane = 0; plane < values.Length; plane++) {
            WriteGraphics(machine.IoPortDispatcher, GraphicsReadMapSelect, (byte)plane);
            values[plane] = machine.Memory.UInt8[address];
        }
        return values;
    }

    private static void WriteDacEntry(IOPortDispatcher ports, byte index, byte red, byte green, byte blue) {
        ports.WriteByte(DacWriteIndexPort, index);
        ports.WriteByte(DacDataPort, red);
        ports.WriteByte(DacDataPort, green);
        ports.WriteByte(DacDataPort, blue);
    }

    private static void WriteSequencer(IOPortDispatcher ports, byte index, byte value) {
        ports.WriteByte(SequencerAddressPort, index);
        ports.WriteByte(SequencerDataPort, value);
    }

    private static void WriteGraphics(IOPortDispatcher ports, byte index, byte value) {
        ports.WriteByte(GraphicsAddressPort, index);
        ports.WriteByte(GraphicsDataPort, value);
    }

    private static void WriteCrt(IOPortDispatcher ports, byte index, byte value) {
        ports.WriteByte(CrtAddressPort, index);
        ports.WriteByte(CrtDataPort, value);
    }

    private static byte ReadCrt(IOPortDispatcher ports, byte index) {
        return ReadIndexed(ports, CrtAddressPort, CrtDataPort, index);
    }

    private static byte ReadIndexed(IOPortDispatcher ports, ushort addressPort, ushort dataPort, byte index) {
        ports.WriteByte(addressPort, index);
        return ports.ReadByte(dataPort);
    }
}
