namespace Spice86.Tests.UI;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.Devices.Video;
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
        MemoryBitmapView view = new();
        view.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void MemoryBitmapViewModel_DefaultProperties() {
        (MemoryBitmapViewModel viewModel, Memory _) = CreateMemoryBitmapViewModel();

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
        (MemoryBitmapViewModel viewModel, Memory _) = CreateMemoryBitmapViewModel();

        viewModel.SelectedVideoMode = MemoryBitmapVideoMode.Text;

        viewModel.BitmapWidth.Should().Be(80);
        viewModel.BitmapHeight.Should().Be(25);
        viewModel.StartAddress.Should().Be("B8000");
        viewModel.WidthLabel.Should().Be("Columns");
        viewModel.HeightLabel.Should().Be("Rows");
    }

    [AvaloniaFact]
    public void MemoryBitmapViewModel_SwitchToCga4ColorMode() {
        (MemoryBitmapViewModel viewModel, Memory _) = CreateMemoryBitmapViewModel();

        viewModel.SelectedVideoMode = MemoryBitmapVideoMode.Cga4Color;

        viewModel.BitmapWidth.Should().Be(320);
        viewModel.BitmapHeight.Should().Be(200);
        viewModel.StartAddress.Should().Be("B8000");
    }

    [AvaloniaFact]
    public void MemoryBitmapViewModel_SwitchToCga2ColorMode() {
        (MemoryBitmapViewModel viewModel, Memory _) = CreateMemoryBitmapViewModel();

        viewModel.SelectedVideoMode = MemoryBitmapVideoMode.Cga2Color;

        viewModel.BitmapWidth.Should().Be(640);
        viewModel.BitmapHeight.Should().Be(200);
        viewModel.StartAddress.Should().Be("B8000");
    }

    [AvaloniaFact]
    public void MemoryBitmapViewModel_SwitchToEga16ColorMode() {
        (MemoryBitmapViewModel viewModel, Memory _) = CreateMemoryBitmapViewModel();

        viewModel.SelectedVideoMode = MemoryBitmapVideoMode.Ega16Color;

        viewModel.BitmapWidth.Should().Be(640);
        viewModel.BitmapHeight.Should().Be(350);
        viewModel.StartAddress.Should().Be("A0000");
    }

    [AvaloniaFact]
    public void MemoryBitmapViewModel_RenderProducesBitmap() {
        (MemoryBitmapViewModel viewModel, Memory memory) = CreateMemoryBitmapViewModel();
        viewModel.IsVisible = true;

        // Write some test data to VGA memory area (0xA0000)
        byte[] testData = new byte[320 * 200];
        for (int i = 0; i < testData.Length; i++) {
            testData[i] = (byte)(i % 256);
        }
        memory.WriteRam(testData, 0xA0000);

        viewModel.RenderBitmapCommand.Execute(null);
        ProcessUiEvents();

        viewModel.RenderedBitmap.Should().NotBeNull();
        viewModel.RenderedBitmap?.PixelSize.Width.Should().Be(320);
        viewModel.RenderedBitmap?.PixelSize.Height.Should().Be(200);
        viewModel.StatusText.Should().Contain("320x200");
    }

    [AvaloniaFact]
    public void MemoryBitmapViewModel_RenderTextModeProducesBitmap() {
        (MemoryBitmapViewModel viewModel, Memory memory) = CreateMemoryBitmapViewModel();
        viewModel.IsVisible = true;

        viewModel.SelectedVideoMode = MemoryBitmapVideoMode.Text;

        // Write text mode data at 0xB8000 (char + attribute pairs)
        byte[] textData = new byte[80 * 25 * 2];
        for (int i = 0; i < 80 * 25; i++) {
            textData[i * 2] = (byte)('A' + i % 26);
            textData[i * 2 + 1] = 0x07;
        }
        memory.WriteRam(textData, 0xB8000);

        viewModel.RenderBitmapCommand.Execute(null);
        ProcessUiEvents();

        viewModel.RenderedBitmap.Should().NotBeNull();
        // Text mode: 80 chars x 8 px = 640, 25 rows x 16 px = 400
        viewModel.RenderedBitmap?.PixelSize.Width.Should().Be(640);
        viewModel.RenderedBitmap?.PixelSize.Height.Should().Be(400);
    }

    [AvaloniaFact]
    public void MemoryBitmapViewModel_InvalidAddressShowsError() {
        (MemoryBitmapViewModel viewModel, Memory _) = CreateMemoryBitmapViewModel();
        viewModel.IsVisible = true;

        viewModel.StartAddress = "ZZZZ";
        viewModel.RenderBitmapCommand.Execute(null);
        ProcessUiEvents();

        viewModel.StatusText.Should().Contain("Invalid");
    }

    [AvaloniaFact]
    public void MemoryBitmapViewModel_InvalidDimensionsShowError() {
        (MemoryBitmapViewModel viewModel, Memory _) = CreateMemoryBitmapViewModel();
        viewModel.IsVisible = true;

        viewModel.BitmapWidth = 0;
        viewModel.RenderBitmapCommand.Execute(null);
        ProcessUiEvents();

        viewModel.StatusText.Should().Contain("positive");
    }

    [AvaloniaFact]
    public void MemoryBitmapViewModel_AutoRefreshUpdatesOnTimer() {
        (MemoryBitmapViewModel viewModel, Memory memory) = CreateMemoryBitmapViewModel();
        viewModel.IsVisible = true;

        byte[] testData = new byte[320 * 200];
        for (int i = 0; i < testData.Length; i++) {
            testData[i] = (byte)(i % 256);
        }
        memory.WriteRam(testData, 0xA0000);

        viewModel.AutoRefresh = true;
        viewModel.UpdateValues(null, EventArgs.Empty);
        ProcessUiEvents();

        viewModel.RenderedBitmap.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void MemoryBitmapViewModel_AutoRefreshDisabledSkipsUpdate() {
        (MemoryBitmapViewModel viewModel, Memory _) = CreateMemoryBitmapViewModel();
        viewModel.IsVisible = true;

        viewModel.AutoRefresh = false;
        viewModel.UpdateValues(null, EventArgs.Empty);
        ProcessUiEvents();

        viewModel.RenderedBitmap.Should().BeNull();
    }

    [AvaloniaFact]
    public void MemoryBitmapViewModel_UseCurrentPaletteToggle() {
        (MemoryBitmapViewModel viewModel, Memory memory) = CreateMemoryBitmapViewModel();
        viewModel.IsVisible = true;

        byte[] testData = new byte[320 * 200];
        memory.WriteRam(testData, 0xA0000);

        viewModel.UseCurrentPalette = true;
        viewModel.RenderBitmapCommand.Execute(null);
        ProcessUiEvents();
        viewModel.RenderedBitmap.Should().NotBeNull();

        viewModel.UseCurrentPalette = false;
        viewModel.RenderBitmapCommand.Execute(null);
        ProcessUiEvents();
        viewModel.RenderedBitmap.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void MemoryBitmapViewModel_Raw8BppMode() {
        (MemoryBitmapViewModel viewModel, Memory memory) = CreateMemoryBitmapViewModel();
        viewModel.IsVisible = true;

        viewModel.SelectedVideoMode = MemoryBitmapVideoMode.Raw8Bpp;

        byte[] testData = new byte[320 * 200];
        for (int i = 0; i < testData.Length; i++) {
            testData[i] = (byte)(i % 256);
        }
        memory.WriteRam(testData, 0xA0000);

        viewModel.RenderBitmapCommand.Execute(null);
        ProcessUiEvents();

        viewModel.RenderedBitmap.Should().NotBeNull();
        viewModel.RenderedBitmap?.PixelSize.Width.Should().Be(320);
        viewModel.RenderedBitmap?.PixelSize.Height.Should().Be(200);
    }

    [AvaloniaFact]
    public void MemoryBitmapViewModel_HexAddressWithPrefix() {
        (MemoryBitmapViewModel viewModel, Memory memory) = CreateMemoryBitmapViewModel();
        viewModel.IsVisible = true;

        byte[] testData = new byte[320 * 200];
        memory.WriteRam(testData, 0xA0000);

        viewModel.StartAddress = "0xA0000";
        viewModel.RenderBitmapCommand.Execute(null);
        ProcessUiEvents();

        viewModel.RenderedBitmap.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void MemoryBitmapViewModel_IsVisibleFalseSkipsUpdate() {
        (MemoryBitmapViewModel viewModel, Memory _) = CreateMemoryBitmapViewModel();
        viewModel.IsVisible = false;

        viewModel.UpdateValues(null, EventArgs.Empty);
        ProcessUiEvents();

        viewModel.RenderedBitmap.Should().BeNull();
    }
}
