namespace Spice86.Tests.InterruptHandlers.Vbe;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.InterruptHandlers.VGA;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Records;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using Xunit;

/// <summary>
/// Unit tests for VESA VBE handler functionality.
/// These tests verify VBE behavior at the unit level, testing individual functions
/// and their interactions with memory and CPU state.
/// </summary>
public class VbeUnitTests {
    private readonly VesaVbeHandler _vesaVbeHandler;
    private readonly State _state;
    private readonly IIndexable _memory;
    private readonly ILoggerService _loggerService;
    private readonly IVgaFunctionality _vgaFunctionality;

    public VbeUnitTests() {
        // Arrange - Setup memory and state
        _state = new State(CpuModel.INTEL_80286);
        A20Gate a20Gate = new A20Gate(false);
        Memory memory = new Memory(new(), new Ram(A20Gate.EndOfHighMemoryArea), a20Gate);
        _memory = memory;
        _loggerService = Substitute.For<ILoggerService>();
        _vgaFunctionality = Substitute.For<IVgaFunctionality>();

        // Create VBE handler
        _vesaVbeHandler = new VesaVbeHandler(_state, _memory, _loggerService, _vgaFunctionality);
    }

    /// <summary>
    /// Tests that ReturnControllerInfo sets the correct VBE signature.
    /// </summary>
    [Fact]
    public void ReturnControllerInfo_ShouldSetVesaSignature() {
        // Arrange
        uint bufferAddress = 0x20000;
        _state.ES = 0x2000;
        _state.DI = 0x0000;

        // Act
        _vesaVbeHandler.ReturnControllerInfo();

        // Assert
        VbeInfoBlock infoBlock = new VbeInfoBlock(_memory, bufferAddress);
        infoBlock.VbeSignature.Should().Be(0x41534556, "VBE signature should be 'VESA'");
        _state.AX.Should().Be(0x004F, "AX should indicate success");
    }

    /// <summary>
    /// Tests that ReturnControllerInfo sets the correct VBE version.
    /// </summary>
    [Fact]
    public void ReturnControllerInfo_ShouldSetVersion10() {
        // Arrange
        uint bufferAddress = 0x20000;
        _state.ES = 0x2000;
        _state.DI = 0x0000;

        // Act
        _vesaVbeHandler.ReturnControllerInfo();

        // Assert
        VbeInfoBlock infoBlock = new VbeInfoBlock(_memory, bufferAddress);
        infoBlock.VbeVersion.Should().Be(0x0100, "VBE version should be 1.0");
    }

    /// <summary>
    /// Tests that ReturnControllerInfo sets total memory correctly.
    /// </summary>
    [Fact]
    public void ReturnControllerInfo_ShouldSetTotalMemory() {
        // Arrange
        uint bufferAddress = 0x20000;
        _state.ES = 0x2000;
        _state.DI = 0x0000;

        // Act
        _vesaVbeHandler.ReturnControllerInfo();

        // Assert
        VbeInfoBlock infoBlock = new VbeInfoBlock(_memory, bufferAddress);
        infoBlock.TotalMemory.Should().Be(4, "Total memory should be 4 * 64KB = 256KB");
    }

    /// <summary>
    /// Tests that ReturnModeInfo for supported mode returns success.
    /// </summary>
    [Fact]
    public void ReturnModeInfo_ForSupportedMode_ShouldReturnSuccess() {
        // Arrange
        _state.ES = 0x2000;
        _state.DI = 0x0000;
        _state.CX = 0x0100; // Mode 0x100

        // Act
        _vesaVbeHandler.ReturnModeInfo();

        // Assert
        _state.AX.Should().Be(0x004F, "AX should indicate success");
    }

    /// <summary>
    /// Tests that ReturnModeInfo for mode 0x101 sets correct resolution.
    /// </summary>
    [Fact]
    public void ReturnModeInfo_Mode101_ShouldSet640x480Resolution() {
        // Arrange
        uint bufferAddress = 0x20000;
        _state.ES = 0x2000;
        _state.DI = 0x0000;
        _state.CX = 0x0101; // Mode 0x101

        // Act
        _vesaVbeHandler.ReturnModeInfo();

        // Assert
        ModeInfoBlock modeInfo = new ModeInfoBlock(_memory, bufferAddress);
        modeInfo.Width.Should().Be(640, "Width should be 640");
        modeInfo.Height.Should().Be(480, "Height should be 480");
        modeInfo.BitsPerPixel.Should().Be(8, "Bits per pixel should be 8");
    }

    /// <summary>
    /// Tests that ReturnModeInfo for unsupported mode returns failure.
    /// </summary>
    [Fact]
    public void ReturnModeInfo_ForUnsupportedMode_ShouldReturnFailure() {
        // Arrange
        _state.ES = 0x2000;
        _state.DI = 0x0000;
        _state.CX = 0xFFFF; // Invalid mode

        // Act
        _vesaVbeHandler.ReturnModeInfo();

        // Assert
        _state.AX.Should().Be(0x014F, "AX should indicate failure");
    }

    /// <summary>
    /// Tests that SetVbeMode with supported mode returns success.
    /// </summary>
    [Fact]
    public void SetVbeMode_WithSupportedMode_ShouldReturnSuccess() {
        // Arrange
        _state.BX = 0x0100; // Mode 0x100

        // Act
        _vesaVbeHandler.SetVbeMode();

        // Assert
        _state.AX.Should().Be(0x004F, "AX should indicate success");
    }

    /// <summary>
    /// Tests that SetVbeMode with unsupported mode returns failure.
    /// </summary>
    [Fact]
    public void SetVbeMode_WithUnsupportedMode_ShouldReturnFailure() {
        // Arrange
        _state.BX = 0xFFFF; // Invalid mode

        // Act
        _vesaVbeHandler.SetVbeMode();

        // Assert
        _state.AX.Should().Be(0x014F, "AX should indicate failure");
    }

    /// <summary>
    /// Tests that ReturnCurrentVbeMode returns the mode that was set.
    /// </summary>
    [Fact]
    public void ReturnCurrentVbeMode_AfterSetMode_ShouldReturnSetMode() {
        // Arrange
        _state.BX = 0x0101; // Mode 0x101
        _vesaVbeHandler.SetVbeMode();

        // Act
        _vesaVbeHandler.ReturnCurrentVbeMode();

        // Assert
        _state.BX.Should().Be(0x0101, "BX should contain the current mode");
        _state.AX.Should().Be(0x004F, "AX should indicate success");
    }

    /// <summary>
    /// Tests that SaveRestoreState subfunction 00h returns buffer size.
    /// </summary>
    [Fact]
    public void SaveRestoreState_GetBufferSize_ShouldReturnSize() {
        // Arrange
        _state.DL = 0x00; // Subfunction: get buffer size
        _state.CX = 0x000F; // All states

        // Act
        _vesaVbeHandler.SaveRestoreState();

        // Assert
        _state.BX.Should().BeGreaterThan(0, "BX should contain buffer size");
        _state.AX.Should().Be(0x004F, "AX should indicate success");
    }

    /// <summary>
    /// Tests that SaveRestoreState subfunction 01h (save) returns success.
    /// </summary>
    [Fact]
    public void SaveRestoreState_SaveState_ShouldReturnSuccess() {
        // Arrange
        _state.DL = 0x01; // Subfunction: save
        _state.CX = 0x000F; // All states
        _state.ES = 0x2000;
        _state.BX = 0x0000;

        // Act
        _vesaVbeHandler.SaveRestoreState();

        // Assert
        _state.AX.Should().Be(0x004F, "AX should indicate success");
    }

    /// <summary>
    /// Tests that SaveRestoreState subfunction 02h (restore) returns success.
    /// </summary>
    [Fact]
    public void SaveRestoreState_RestoreState_ShouldReturnSuccess() {
        // Arrange
        _state.DL = 0x02; // Subfunction: restore
        _state.CX = 0x000F; // All states
        _state.ES = 0x2000;
        _state.BX = 0x0000;

        // Act
        _vesaVbeHandler.SaveRestoreState();

        // Assert
        _state.AX.Should().Be(0x004F, "AX should indicate success");
    }

    /// <summary>
    /// Tests that SaveRestoreState with invalid subfunction returns failure.
    /// </summary>
    [Fact]
    public void SaveRestoreState_WithInvalidSubfunction_ShouldReturnFailure() {
        // Arrange
        _state.DL = 0xFF; // Invalid subfunction
        _state.CX = 0x000F;

        // Act
        _vesaVbeHandler.SaveRestoreState();

        // Assert
        _state.AX.Should().Be(0x014F, "AX should indicate failure");
    }

    /// <summary>
    /// Tests VbeInfoBlock structure size.
    /// </summary>
    [Fact]
    public void VbeInfoBlock_ShouldHaveCorrectSize() {
        // Assert
        VbeInfoBlock.StructureSize.Should().Be(256, "VbeInfoBlock should be 256 bytes");
    }

    /// <summary>
    /// Tests ModeInfoBlock structure size.
    /// </summary>
    [Fact]
    public void ModeInfoBlock_ShouldHaveCorrectSize() {
        // Assert
        ModeInfoBlock.StructureSize.Should().Be(256, "ModeInfoBlock should be 256 bytes");
    }

    /// <summary>
    /// Tests that mode attributes are set correctly for graphics mode.
    /// </summary>
    [Fact]
    public void ReturnModeInfo_ShouldSetGraphicsModeAttributes() {
        // Arrange
        uint bufferAddress = 0x20000;
        _state.ES = 0x2000;
        _state.DI = 0x0000;
        _state.CX = 0x0101; // Mode 0x101

        // Act
        _vesaVbeHandler.ReturnModeInfo();

        // Assert
        ModeInfoBlock modeInfo = new ModeInfoBlock(_memory, bufferAddress);
        modeInfo.ModeAttributes.Should().HaveFlag(VbeModeAttribute.ModeSupported, "Mode should be supported");
        modeInfo.ModeAttributes.Should().HaveFlag(VbeModeAttribute.ColorMode, "Mode should be color");
        modeInfo.ModeAttributes.Should().HaveFlag(VbeModeAttribute.GraphicsMode, "Mode should be graphics");
    }

    /// <summary>
    /// Tests that window attributes are set correctly.
    /// </summary>
    [Fact]
    public void ReturnModeInfo_ShouldSetWindowAttributes() {
        // Arrange
        uint bufferAddress = 0x20000;
        _state.ES = 0x2000;
        _state.DI = 0x0000;
        _state.CX = 0x0100;

        // Act
        _vesaVbeHandler.ReturnModeInfo();

        // Assert
        ModeInfoBlock modeInfo = new ModeInfoBlock(_memory, bufferAddress);
        modeInfo.WindowAAttributes.Should().HaveFlag(VbeWindowAttribute.WindowExists);
        modeInfo.WindowAAttributes.Should().HaveFlag(VbeWindowAttribute.WindowReadable);
        modeInfo.WindowAAttributes.Should().HaveFlag(VbeWindowAttribute.WindowWritable);
    }

    /// <summary>
    /// Tests that memory model is set correctly for 256-color mode.
    /// </summary>
    [Fact]
    public void ReturnModeInfo_256ColorMode_ShouldSetPackedPixelModel() {
        // Arrange
        uint bufferAddress = 0x20000;
        _state.ES = 0x2000;
        _state.DI = 0x0000;
        _state.CX = 0x0101; // 640x480x256

        // Act
        _vesaVbeHandler.ReturnModeInfo();

        // Assert
        ModeInfoBlock modeInfo = new ModeInfoBlock(_memory, bufferAddress);
        modeInfo.MemoryModel.Should().Be(VbeMemoryModel.PackedPixel, "256-color mode should use packed pixel model");
    }

    /// <summary>
    /// Tests that memory model is set correctly for 16-color mode.
    /// </summary>
    [Fact]
    public void ReturnModeInfo_16ColorMode_ShouldSetPlanarModel() {
        // Arrange
        uint bufferAddress = 0x20000;
        _state.ES = 0x2000;
        _state.DI = 0x0000;
        _state.CX = 0x0102; // 800x600x16

        // Act
        _vesaVbeHandler.ReturnModeInfo();

        // Assert
        ModeInfoBlock modeInfo = new ModeInfoBlock(_memory, bufferAddress);
        modeInfo.MemoryModel.Should().Be(VbeMemoryModel.Planar, "16-color mode should use planar model");
    }
}
