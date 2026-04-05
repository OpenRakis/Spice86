namespace Spice86.ViewModels;

using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Memory;
using Spice86.ViewModels.Services;
using Spice86.ViewModels.Services.Rendering;

using System;
using System.Runtime.InteropServices;

/// <summary>
///     ViewModel for rendering an arbitrary memory region as a bitmap using legacy video modes.
///     For VGA 256-color mode, delegates to the Core VGA renderer for correct output.
///     For other modes (CGA, EGA, raw, text, etc.), uses the custom MemoryBitmapRenderer.
/// </summary>
public partial class MemoryBitmapViewModel : ViewModelBase, IEmulatorObjectViewModel {
    private readonly IMemory _memory;
    private readonly IHostStorageProvider _hostStorageProvider;
    private readonly IUIDispatcher _uiDispatcher;
    private readonly IVgaRenderer _vgaRenderer;

    [ObservableProperty]
    private string _startAddress = "A0000";

    [ObservableProperty]
    private int _bitmapWidth = 320;

    [ObservableProperty]
    private int _bitmapHeight = 200;

    [ObservableProperty]
    private MemoryBitmapVideoMode _selectedVideoMode = MemoryBitmapVideoMode.Vga256Color;

    [ObservableProperty]
    private WriteableBitmap? _renderedBitmap;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private bool _useCurrentPalette = true;

    [ObservableProperty]
    private bool _autoRefresh = true;

    private readonly uint[]? _dacPaletteMap;

    /// <summary>
    ///     Available video modes for selection in the UI.
    /// </summary>
    public MemoryBitmapVideoMode[] AvailableVideoModes { get; } = Enum.GetValues<MemoryBitmapVideoMode>();

    /// <summary>
    ///     The width unit label (pixels for graphics modes, chars for text mode).
    /// </summary>
    public string WidthLabel => SelectedVideoMode == MemoryBitmapVideoMode.Text ? "Columns" : "Width (px)";

    /// <summary>
    ///     The height unit label (pixels for graphics modes, chars for text mode).
    /// </summary>
    public string HeightLabel => SelectedVideoMode == MemoryBitmapVideoMode.Text ? "Rows" : "Height (px)";

    /// <summary>
    ///     Creates a new MemoryBitmapViewModel.
    /// </summary>
    /// <param name="memory">The emulator memory to read from.</param>
    /// <param name="hostStorageProvider">The storage provider for file save operations.</param>
    /// <param name="uiDispatcher">The UI dispatcher for thread-safe operations.</param>
    /// <param name="vgaRenderer">The Core VGA renderer for capturing live VGA output.</param>
    /// <param name="dacPaletteMap">The current VGA DAC palette map (256 ARGB entries), or null to use defaults.</param>
    public MemoryBitmapViewModel(IMemory memory, IHostStorageProvider hostStorageProvider,
        IUIDispatcher uiDispatcher, IVgaRenderer vgaRenderer, uint[]? dacPaletteMap) {
        _memory = memory;
        _hostStorageProvider = hostStorageProvider;
        _uiDispatcher = uiDispatcher;
        _vgaRenderer = vgaRenderer;
        _dacPaletteMap = dacPaletteMap;
    }

    /// <inheritdoc />
    public bool IsVisible { get; set; }

    /// <inheritdoc />
    public void UpdateValues(object? sender, EventArgs e) {
        if (!IsVisible || !AutoRefresh) {
            return;
        }
        RenderBitmap();
    }

    partial void OnSelectedVideoModeChanged(MemoryBitmapVideoMode value) {
        OnPropertyChanged(nameof(WidthLabel));
        OnPropertyChanged(nameof(HeightLabel));
        ApplyPresetForMode(value);
    }

    /// <summary>
    ///     Applies sensible default dimensions when switching video modes.
    /// </summary>
    private void ApplyPresetForMode(MemoryBitmapVideoMode mode) {
        switch (mode) {
            case MemoryBitmapVideoMode.Vga256Color:
                BitmapWidth = _vgaRenderer.Width;
                BitmapHeight = _vgaRenderer.Height;
                StartAddress = "A0000";
                break;
            case MemoryBitmapVideoMode.VgaModeX:
                BitmapWidth = 320;
                BitmapHeight = 240;
                StartAddress = "A0000";
                break;
            case MemoryBitmapVideoMode.Ega16Color:
                BitmapWidth = 640;
                BitmapHeight = 350;
                StartAddress = "A0000";
                break;
            case MemoryBitmapVideoMode.Packed4Bpp:
                BitmapWidth = 320;
                BitmapHeight = 200;
                StartAddress = "A0000";
                break;
            case MemoryBitmapVideoMode.Cga4Color:
                BitmapWidth = 320;
                BitmapHeight = 200;
                StartAddress = "B8000";
                break;
            case MemoryBitmapVideoMode.Cga2Color:
                BitmapWidth = 640;
                BitmapHeight = 200;
                StartAddress = "B8000";
                break;
            case MemoryBitmapVideoMode.Linear1Bpp:
                BitmapWidth = 640;
                BitmapHeight = 480;
                StartAddress = "A0000";
                break;
            case MemoryBitmapVideoMode.Text:
                BitmapWidth = 80;
                BitmapHeight = 25;
                StartAddress = "B8000";
                break;
            case MemoryBitmapVideoMode.Raw8Bpp:
                BitmapWidth = 320;
                BitmapHeight = 200;
                StartAddress = "A0000";
                break;
        }
    }

    /// <summary>
    ///     Renders the current memory region to the bitmap.
    ///     For VGA 256-color mode, captures the frame from the Core VGA renderer.
    ///     For other modes, reads raw memory and interprets using the custom renderer.
    /// </summary>
    [RelayCommand]
    public void RenderBitmap() {
        if (SelectedVideoMode == MemoryBitmapVideoMode.Vga256Color) {
            RenderFromCoreVga();
            return;
        }
        RenderFromRawMemory();
    }

    /// <summary>
    ///     Captures the current frame from the Core VGA renderer.
    ///     This correctly handles all VGA register state including planes, panning, and start address.
    /// </summary>
    private void RenderFromCoreVga() {
        int width = _vgaRenderer.Width;
        int height = _vgaRenderer.Height;
        if (width <= 0 || height <= 0) {
            StatusText = "VGA renderer has no active display";
            return;
        }

        int bufferSize = width * height;
        uint[] frameBuffer = new uint[bufferSize];
        _vgaRenderer.Render(frameBuffer.AsSpan());

        WritePixelsToBitmap(frameBuffer, width, height);
        StatusText = $"Rendered {width}x{height} ({SelectedVideoMode}) — live VGA output";
    }

    /// <summary>
    ///     Renders a memory region using the custom MemoryBitmapRenderer for non-VGA modes.
    /// </summary>
    private void RenderFromRawMemory() {
        if (!TryParseHexAddress(StartAddress, out uint address)) {
            StatusText = "Invalid start address";
            return;
        }

        if (BitmapWidth <= 0 || BitmapHeight <= 0) {
            StatusText = "Width and height must be positive";
            return;
        }

        int outputPixelWidth = MemoryBitmapRenderer.GetOutputPixelWidth(SelectedVideoMode, BitmapWidth);
        int outputPixelHeight = MemoryBitmapRenderer.GetOutputPixelHeight(SelectedVideoMode, BitmapHeight);

        if (outputPixelWidth <= 0 || outputPixelHeight <= 0) {
            StatusText = "Output dimensions must be positive";
            return;
        }

        int estimatedBytes = MemoryBitmapRenderer.EstimateRequiredBytes(SelectedVideoMode, BitmapWidth, BitmapHeight);
        byte[] data = ReadMemoryRegion(address, estimatedBytes);
        uint[]? palette = ResolvePalette();

        uint[] pixels = MemoryBitmapRenderer.Render(data, BitmapWidth, BitmapHeight, SelectedVideoMode, palette);

        if (pixels.Length == 0) {
            StatusText = "Render produced no output";
            return;
        }

        WritePixelsToBitmap(pixels, outputPixelWidth, outputPixelHeight);
        StatusText = $"Rendered {outputPixelWidth}x{outputPixelHeight} ({SelectedVideoMode})";
    }

    /// <summary>
    ///     Saves the current rendered bitmap to disk.
    /// </summary>
    [RelayCommand]
    public async Task SaveBitmap() {
        if (RenderedBitmap is not null) {
            await _hostStorageProvider.SaveBitmapFile(RenderedBitmap);
        }
    }

    private void WritePixelsToBitmap(uint[] pixels, int pixelWidth, int pixelHeight) {
        if (RenderedBitmap is null ||
            RenderedBitmap.PixelSize.Width != pixelWidth ||
            RenderedBitmap.PixelSize.Height != pixelHeight) {
            RenderedBitmap?.Dispose();
            RenderedBitmap = new WriteableBitmap(
                new PixelSize(pixelWidth, pixelHeight),
                new Vector(96, 96),
                Avalonia.Platform.PixelFormat.Bgra8888,
                AlphaFormat.Opaque);
        }

        using ILockedFramebuffer framebuffer = RenderedBitmap.Lock();
        ReadOnlySpan<byte> sourceBytes = MemoryMarshal.AsBytes(pixels.AsSpan());
        int bytesToCopy = Math.Min(sourceBytes.Length, framebuffer.RowBytes * framebuffer.Size.Height);
        unsafe {
            sourceBytes[..bytesToCopy].CopyTo(new Span<byte>((void*)framebuffer.Address, bytesToCopy));
        }
        OnPropertyChanged(nameof(RenderedBitmap));
    }

    private byte[] ReadMemoryRegion(uint address, int length) {
        int memLength = _memory.Length;
        if (address >= memLength) {
            return [];
        }
        int safeLength = (int)Math.Min(length, memLength - address);
        if (safeLength <= 0) {
            return [];
        }
        return _memory.ReadRam((uint)safeLength, address);
    }

    private uint[]? ResolvePalette() {
        if (UseCurrentPalette && _dacPaletteMap is not null) {
            uint[] copy = new uint[_dacPaletteMap.Length];
            Array.Copy(_dacPaletteMap, copy, _dacPaletteMap.Length);
            return copy;
        }
        return null;
    }

    private static bool TryParseHexAddress(string? hex, out uint address) {
        address = 0;
        if (string.IsNullOrWhiteSpace(hex)) {
            return false;
        }

        string trimmed = hex.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
            trimmed = trimmed[2..];
        }

        return uint.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out address);
    }
}
