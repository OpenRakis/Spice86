namespace Spice86.Tests.Video;

using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Devices.Video.Registers.General;
using Spice86.Core.Emulator.Memory;
using Spice86.Logging;

using NSubstitute;

using Xunit;

public class RendererTest {
    [Fact]
    public void TestPixelAspectRatioFor320x200Is5Over4() {
        // Arrange
        var memory = Substitute.For<IMemory>();
        var videoState = new VideoState();
        var loggerService = Substitute.For<LoggerService>();
        
        // Setup video state to return 320x200 resolution (mode 13h)
        videoState.GeneralRegisters.MiscellaneousOutput.ClockSelect = MiscellaneousOutput.ClockSelectValue.Use25175Khz;
        videoState.SequencerRegisters.ClockingModeRegister.HalfDotClock = true;
        // Setting VerticalDisplayEnd to 199 gives height = (199 + 1) / 1 = 200
        videoState.CrtControllerRegisters.VerticalDisplayEnd = 199;
        videoState.CrtControllerRegisters.MaximumScanlineRegister.CrtcScanDouble = false;
        
        var renderer = new Renderer(memory, videoState, loggerService);
        
        // Act
        int width = renderer.Width;
        int height = renderer.Height;
        double pixelAspectRatio = renderer.PixelAspectRatio;
        
        // Assert
        Assert.Equal(320, width);
        Assert.Equal(200, height);
        Assert.Equal(1.25, pixelAspectRatio); // 5/4 = 1.25
    }
    
    [Fact]
    public void TestPixelAspectRatioFor640x480IsSquare() {
        // Arrange
        var memory = Substitute.For<IMemory>();
        var videoState = new VideoState();
        var loggerService = Substitute.For<LoggerService>();
        
        // Setup video state to return 640x480 resolution
        videoState.GeneralRegisters.MiscellaneousOutput.ClockSelect = MiscellaneousOutput.ClockSelectValue.Use25175Khz;
        videoState.SequencerRegisters.ClockingModeRegister.HalfDotClock = false;
        // Setting VerticalDisplayEnd to 479 gives height = (479 + 1) / 1 = 480
        // 479 = 0x1DF, lower 8 bits = 0xDF = 223, bit 8 = 1, bit 9 = 0
        videoState.CrtControllerRegisters.VerticalDisplayEnd = 223;
        // OverflowRegister: bit 1 for VerticalDisplayEnd bit 8, bit 6 for VerticalDisplayEnd bit 9
        videoState.CrtControllerRegisters.OverflowRegister[1] = true; // bit 8 = 1
        videoState.CrtControllerRegisters.OverflowRegister[6] = false; // bit 9 = 0
        videoState.CrtControllerRegisters.MaximumScanlineRegister.CrtcScanDouble = false;
        
        var renderer = new Renderer(memory, videoState, loggerService);
        
        // Act
        int width = renderer.Width;
        int height = renderer.Height;
        double pixelAspectRatio = renderer.PixelAspectRatio;
        
        // Assert
        Assert.Equal(640, width);
        Assert.Equal(480, height);
        Assert.Equal(1.0, pixelAspectRatio); // Square pixels for non-320x200 modes
    }
}
