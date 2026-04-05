namespace Spice86.Tests.UI;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using FluentAssertions;

using NSubstitute;

using Spice86.ViewModels.Services.Rendering;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.ViewModels;
using Spice86.ViewModels.Services;
using Spice86.Views;

using Xunit;

/// <summary>
///     UI integration tests for the MemoryBitmapView and MemoryBitmapViewModel.
/// </summary>
public class MemoryBitmapViewUiTests : BreakpointUiTestBase {
    private (MemoryBitmapViewModel viewModel, Memory memory) CreateMemoryBitmapViewModel() {
        (Memory memory, AddressReadWriteBreakpoints _, AddressReadWriteBreakpoints _) = CreateMemory();
        IHostStorageProvider hostStorageProvider = Substitute.For<IHostStorageProvider>();
        UIDispatcher uiDispatcher = CreateUIDispatcher();
        uint[] palette = MemoryBitmapRenderer.DefaultVga256Palette;

        MemoryBitmapViewModel viewModel = new(memory, hostStorageProvider, uiDispatcher, palette);

        return (viewModel, memory);
    }

    [AvaloniaFact]
    public void MemoryBitmapView_CanBeCreated() {
        // Arrange & Act
        MemoryBitmapView view = new();

        // Assert
        view.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void MemoryBitmapViewModel_DefaultProperties() {
        // Arrange & Act
        (MemoryBitmapViewModel viewModel, Memory _) = CreateMemoryBitmapViewModel();

        // Assert
        viewModel.SelectedVideoMode.Should().Be(MemoryBitmapVideoMode.Vga256Color);
        viewModel.BitmapWidth.Should().Be(320);
        viewModel.BitmapHeight.Should().Be(200);
        viewModel.StartAddress.Should().Be("A0000");
        viewModel.AutoRefresh.Should().BeTrue();
        viewModel.UseCurrentPalette.Should().BeTrue();
        viewModel.AvailableVideoModes.Should().NotBeEmpty();
    }

    [AvaloniaFact]
    public void MemoryBitmapViewModel_SwitchToTextMode() {
        // Arrange
        (MemoryBitmapViewModel viewModel, Memory _) = CreateMemoryBitmapViewModel();

        // Act
        viewModel.SelectedVideoMode = MemoryBitmapVideoMode.Text;

        // Assert
        viewModel.BitmapWidth.Should().Be(80);
        viewModel.BitmapHeight.Should().Be(25);
        viewModel.StartAddress.Should().Be("B8000");
        viewModel.WidthLabel.Should().Be("Columns");
        viewModel.HeightLabel.Should().Be("Rows");
    }

    [AvaloniaFact]
    public void MemoryBitmapViewModel_SwitchToCga4ColorMode() {
        // Arrange
        (MemoryBitmapViewModel viewModel, Memory _) = CreateMemoryBitmapViewModel();

        // Act
        viewModel.SelectedVideoMode = MemoryBitmapVideoMode.Cga4Color;

        // Assert
        viewModel.BitmapWidth.Should().Be(320);
        viewModel.BitmapHeight.Should().Be(200);
        viewModel.StartAddress.Should().Be("B8000");
    }

    [AvaloniaFact]
    public void MemoryBitmapViewModel_SwitchToCga2ColorMode() {
        // Arrange
        (MemoryBitmapViewModel viewModel, Memory _) = CreateMemoryBitmapViewModel();

        // Act
        viewModel.SelectedVideoMode = MemoryBitmapVideoMode.Cga2Color;

        // Assert
        viewModel.BitmapWidth.Should().Be(640);
        viewModel.BitmapHeight.Should().Be(200);
        viewModel.StartAddress.Should().Be("B8000");
    }

    [AvaloniaFact]
    public void MemoryBitmapViewModel_SwitchToEga16ColorMode() {
        // Arrange
        (MemoryBitmapViewModel viewModel, Memory _) = CreateMemoryBitmapViewModel();

        // Act
        viewModel.SelectedVideoMode = MemoryBitmapVideoMode.Ega16Color;

        // Assert
        viewModel.BitmapWidth.Should().Be(640);
        viewModel.BitmapHeight.Should().Be(350);
        viewModel.StartAddress.Should().Be("A0000");
    }

    [AvaloniaFact]
    public void MemoryBitmapViewModel_RenderProducesBitmap() {
        // Arrange
        (MemoryBitmapViewModel viewModel, Memory memory) = CreateMemoryBitmapViewModel();
        viewModel.IsVisible = true;
        byte[] testData = new byte[320 * 200];
        for (int i = 0; i < testData.Length; i++) {
            testData[i] = (byte)(i % 256);
        }
        memory.WriteRam(testData, 0xA0000);

        // Act
        viewModel.RenderBitmapCommand.Execute(null);
        ProcessUiEvents();

        // Assert
        viewModel.RenderedBitmap.Should().NotBeNull();
        viewModel.RenderedBitmap?.PixelSize.Width.Should().Be(320);
        viewModel.RenderedBitmap?.PixelSize.Height.Should().Be(200);
        viewModel.StatusText.Should().Contain("320x200");
    }

    [AvaloniaFact]
    public void MemoryBitmapViewModel_RenderTextModeProducesBitmap() {
        // Arrange
        (MemoryBitmapViewModel viewModel, Memory memory) = CreateMemoryBitmapViewModel();
        viewModel.IsVisible = true;
        viewModel.SelectedVideoMode = MemoryBitmapVideoMode.Text;
        byte[] textData = new byte[80 * 25 * 2];
        for (int i = 0; i < 80 * 25; i++) {
            textData[i * 2] = (byte)('A' + i % 26);
            textData[i * 2 + 1] = 0x07;
        }
        memory.WriteRam(textData, 0xB8000);

        // Act
        viewModel.RenderBitmapCommand.Execute(null);
        ProcessUiEvents();

        // Assert
        viewModel.RenderedBitmap.Should().NotBeNull();
        viewModel.RenderedBitmap?.PixelSize.Width.Should().Be(640);
        viewModel.RenderedBitmap?.PixelSize.Height.Should().Be(400);
    }

    [AvaloniaFact]
    public void MemoryBitmapViewModel_InvalidAddressShowsError() {
        // Arrange
        (MemoryBitmapViewModel viewModel, Memory _) = CreateMemoryBitmapViewModel();
        viewModel.IsVisible = true;

        // Act
        viewModel.StartAddress = "ZZZZ";
        viewModel.RenderBitmapCommand.Execute(null);
        ProcessUiEvents();

        // Assert
        viewModel.StatusText.Should().Contain("Invalid");
    }

    [AvaloniaFact]
    public void MemoryBitmapViewModel_InvalidDimensionsShowError() {
        // Arrange
        (MemoryBitmapViewModel viewModel, Memory _) = CreateMemoryBitmapViewModel();
        viewModel.IsVisible = true;

        // Act
        viewModel.BitmapWidth = 0;
        viewModel.RenderBitmapCommand.Execute(null);
        ProcessUiEvents();

        // Assert
        viewModel.StatusText.Should().Contain("positive");
    }

    [AvaloniaFact]
    public void MemoryBitmapViewModel_AutoRefreshUpdatesOnTimer() {
        // Arrange
        (MemoryBitmapViewModel viewModel, Memory memory) = CreateMemoryBitmapViewModel();
        viewModel.IsVisible = true;
        byte[] testData = new byte[320 * 200];
        for (int i = 0; i < testData.Length; i++) {
            testData[i] = (byte)(i % 256);
        }
        memory.WriteRam(testData, 0xA0000);
        viewModel.AutoRefresh = true;

        // Act
        viewModel.UpdateValues(null, EventArgs.Empty);
        ProcessUiEvents();

        // Assert
        viewModel.RenderedBitmap.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void MemoryBitmapViewModel_AutoRefreshDisabledSkipsUpdate() {
        // Arrange
        (MemoryBitmapViewModel viewModel, Memory _) = CreateMemoryBitmapViewModel();
        viewModel.IsVisible = true;
        viewModel.AutoRefresh = false;

        // Act
        viewModel.UpdateValues(null, EventArgs.Empty);
        ProcessUiEvents();

        // Assert
        viewModel.RenderedBitmap.Should().BeNull();
    }

    [AvaloniaFact]
    public void MemoryBitmapViewModel_UseCurrentPaletteToggle() {
        // Arrange
        (MemoryBitmapViewModel viewModel, Memory memory) = CreateMemoryBitmapViewModel();
        viewModel.IsVisible = true;
        byte[] testData = new byte[320 * 200];
        memory.WriteRam(testData, 0xA0000);

        // Act - render with current palette
        viewModel.UseCurrentPalette = true;
        viewModel.RenderBitmapCommand.Execute(null);
        ProcessUiEvents();

        // Assert
        viewModel.RenderedBitmap.Should().NotBeNull();

        // Act - render with default palette
        viewModel.UseCurrentPalette = false;
        viewModel.RenderBitmapCommand.Execute(null);
        ProcessUiEvents();

        // Assert
        viewModel.RenderedBitmap.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void MemoryBitmapViewModel_Raw8BppMode() {
        // Arrange
        (MemoryBitmapViewModel viewModel, Memory memory) = CreateMemoryBitmapViewModel();
        viewModel.IsVisible = true;
        viewModel.SelectedVideoMode = MemoryBitmapVideoMode.Raw8Bpp;
        byte[] testData = new byte[320 * 200];
        for (int i = 0; i < testData.Length; i++) {
            testData[i] = (byte)(i % 256);
        }
        memory.WriteRam(testData, 0xA0000);

        // Act
        viewModel.RenderBitmapCommand.Execute(null);
        ProcessUiEvents();

        // Assert
        viewModel.RenderedBitmap.Should().NotBeNull();
        viewModel.RenderedBitmap?.PixelSize.Width.Should().Be(320);
        viewModel.RenderedBitmap?.PixelSize.Height.Should().Be(200);
    }

    [AvaloniaFact]
    public void MemoryBitmapViewModel_HexAddressWithPrefix() {
        // Arrange
        (MemoryBitmapViewModel viewModel, Memory memory) = CreateMemoryBitmapViewModel();
        viewModel.IsVisible = true;
        byte[] testData = new byte[320 * 200];
        memory.WriteRam(testData, 0xA0000);

        // Act
        viewModel.StartAddress = "0xA0000";
        viewModel.RenderBitmapCommand.Execute(null);
        ProcessUiEvents();

        // Assert
        viewModel.RenderedBitmap.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void MemoryBitmapViewModel_IsVisibleFalseSkipsUpdate() {
        // Arrange
        (MemoryBitmapViewModel viewModel, Memory _) = CreateMemoryBitmapViewModel();
        viewModel.IsVisible = false;

        // Act
        viewModel.UpdateValues(null, EventArgs.Empty);
        ProcessUiEvents();

        // Assert
        viewModel.RenderedBitmap.Should().BeNull();
    }

    [AvaloniaFact]
    public void MemoryBitmapViewModel_SwitchToVgaModeX() {
        // Arrange
        (MemoryBitmapViewModel viewModel, Memory _) = CreateMemoryBitmapViewModel();

        // Act
        viewModel.SelectedVideoMode = MemoryBitmapVideoMode.VgaModeX;

        // Assert
        viewModel.BitmapWidth.Should().Be(320);
        viewModel.BitmapHeight.Should().Be(240);
        viewModel.StartAddress.Should().Be("A0000");
    }

    [AvaloniaFact]
    public void MemoryBitmapViewModel_SwitchToPacked4Bpp() {
        // Arrange
        (MemoryBitmapViewModel viewModel, Memory _) = CreateMemoryBitmapViewModel();

        // Act
        viewModel.SelectedVideoMode = MemoryBitmapVideoMode.Packed4Bpp;

        // Assert
        viewModel.BitmapWidth.Should().Be(320);
        viewModel.BitmapHeight.Should().Be(200);
        viewModel.StartAddress.Should().Be("A0000");
    }

    [AvaloniaFact]
    public void MemoryBitmapViewModel_SwitchToLinear1Bpp() {
        // Arrange
        (MemoryBitmapViewModel viewModel, Memory _) = CreateMemoryBitmapViewModel();

        // Act
        viewModel.SelectedVideoMode = MemoryBitmapVideoMode.Linear1Bpp;

        // Assert
        viewModel.BitmapWidth.Should().Be(640);
        viewModel.BitmapHeight.Should().Be(480);
        viewModel.StartAddress.Should().Be("A0000");
    }

    [AvaloniaFact]
    public void MemoryBitmapViewModel_RenderVgaModeX() {
        // Arrange
        (MemoryBitmapViewModel viewModel, Memory memory) = CreateMemoryBitmapViewModel();
        viewModel.IsVisible = true;
        viewModel.SelectedVideoMode = MemoryBitmapVideoMode.VgaModeX;
        byte[] testData = new byte[320 * 240];
        memory.WriteRam(testData, 0xA0000);

        // Act
        viewModel.RenderBitmapCommand.Execute(null);
        ProcessUiEvents();

        // Assert
        viewModel.RenderedBitmap.Should().NotBeNull();
        viewModel.RenderedBitmap?.PixelSize.Width.Should().Be(320);
        viewModel.RenderedBitmap?.PixelSize.Height.Should().Be(240);
    }

    [AvaloniaFact]
    public void MemoryBitmapViewModel_RenderLinear1Bpp() {
        // Arrange
        (MemoryBitmapViewModel viewModel, Memory memory) = CreateMemoryBitmapViewModel();
        viewModel.IsVisible = true;
        viewModel.SelectedVideoMode = MemoryBitmapVideoMode.Linear1Bpp;
        byte[] testData = new byte[80 * 480];
        memory.WriteRam(testData, 0xA0000);

        // Act
        viewModel.RenderBitmapCommand.Execute(null);
        ProcessUiEvents();

        // Assert
        viewModel.RenderedBitmap.Should().NotBeNull();
        viewModel.RenderedBitmap?.PixelSize.Width.Should().Be(640);
        viewModel.RenderedBitmap?.PixelSize.Height.Should().Be(480);
    }
}
