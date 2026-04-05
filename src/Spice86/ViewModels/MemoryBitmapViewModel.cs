namespace Spice86.ViewModels;

using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using AvaloniaHex.Document;
using AvaloniaHex.Editing;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator.Memory;
using Spice86.ViewModels.DataModels;
using Spice86.ViewModels.Services;
using Spice86.ViewModels.Services.Rendering;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

/// <summary>
///     ViewModel for rendering an arbitrary memory region as a bitmap using legacy video modes.
///     Embeds a HexEditor for memory browsing with a GridSplitter, keeping per-mode state.
///     All modes use the custom MemoryBitmapRenderer to interpret raw memory bytes.
/// </summary>
public partial class MemoryBitmapViewModel : ViewModelBase, IEmulatorObjectViewModel {
    private sealed class VideoModeSettings {
        public string StartAddress { get; set; } = "A0000";
        public int Width { get; set; } = 320;
        public int Height { get; set; } = 200;
    }

    private readonly IMemory _memory;
    private readonly IHostStorageProvider _hostStorageProvider;
    private readonly IUIDispatcher _uiDispatcher;
    private readonly uint[]? _dacPaletteMap;
    private readonly Dictionary<MemoryBitmapVideoMode, VideoModeSettings> _perModeSettings = new();

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

    [ObservableProperty]
    private DataMemoryDocument? _hexDocument;

    [ObservableProperty]
    private string _hexSelectionInfo = "";

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
    ///     The address range currently being rendered.
    /// </summary>
    public string AddressRangeDisplay {
        get {
            if (!TryParseHexAddress(StartAddress, out uint address)) {
                return "Invalid";
            }
            int estimatedBytes = MemoryBitmapRenderer.EstimateRequiredBytes(SelectedVideoMode, BitmapWidth, BitmapHeight);
            uint endAddress = address + (uint)estimatedBytes;
            return $"{address:X5}-{endAddress:X5}";
        }
    }

    /// <summary>
    ///     Estimated bytes needed for the current mode/dimensions.
    /// </summary>
    public string EstimatedBytesDisplay {
        get {
            int bytes = MemoryBitmapRenderer.EstimateRequiredBytes(SelectedVideoMode, BitmapWidth, BitmapHeight);
            return $"{bytes:N0}";
        }
    }

    /// <summary>
    ///     Output pixel dimensions for the current mode.
    /// </summary>
    public string OutputDimensionsDisplay {
        get {
            int w = MemoryBitmapRenderer.GetOutputPixelWidth(SelectedVideoMode, BitmapWidth);
            int h = MemoryBitmapRenderer.GetOutputPixelHeight(SelectedVideoMode, BitmapHeight);
            return $"{w}x{h}";
        }
    }

    /// <summary>
    ///     Creates a new MemoryBitmapViewModel.
    /// </summary>
    /// <param name="memory">The emulator memory to read from.</param>
    /// <param name="hostStorageProvider">The storage provider for file save operations.</param>
    /// <param name="uiDispatcher">The UI dispatcher for thread-safe operations.</param>
    /// <param name="dacPaletteMap">The current VGA DAC palette map (256 ARGB entries), or null to use defaults.</param>
    public MemoryBitmapViewModel(IMemory memory, IHostStorageProvider hostStorageProvider,
        IUIDispatcher uiDispatcher, uint[]? dacPaletteMap) {
        _memory = memory;
        _hostStorageProvider = hostStorageProvider;
        _uiDispatcher = uiDispatcher;
        _dacPaletteMap = dacPaletteMap;
        InitializePerModeDefaults();
        UpdateHexDocument();
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

    /// <summary>
    ///     Handles hex editor selection changes - updates selection info display.
    /// </summary>
    public void OnHexSelectionRangeChanged(object? sender, EventArgs e) {
        if (sender is Selection selection && TryParseHexAddress(StartAddress, out uint baseAddr)) {
            ulong selStart = (ulong)selection.Range.Start.ByteIndex;
            ulong selEnd = (ulong)selection.Range.End.ByteIndex;
            ulong length = selEnd > selStart ? selEnd - selStart : 0;
            HexSelectionInfo = length > 0
                ? $"Selected: {baseAddr + selStart:X5}-{baseAddr + selEnd:X5} ({length:N0} bytes)"
                : "";
        }
    }

    partial void OnSelectedVideoModeChanged(MemoryBitmapVideoMode oldValue, MemoryBitmapVideoMode newValue) {
        SaveModeSettings(oldValue);
        RestoreModeSettings(newValue);
        OnPropertyChanged(nameof(WidthLabel));
        OnPropertyChanged(nameof(HeightLabel));
        RefreshInfoDisplays();
        UpdateHexDocument();
    }

    partial void OnStartAddressChanged(string value) {
        RefreshInfoDisplays();
        UpdateHexDocument();
    }

    partial void OnBitmapWidthChanged(int value) {
        RefreshInfoDisplays();
        UpdateHexDocument();
    }

    partial void OnBitmapHeightChanged(int value) {
        RefreshInfoDisplays();
        UpdateHexDocument();
    }

    private void RefreshInfoDisplays() {
        OnPropertyChanged(nameof(AddressRangeDisplay));
        OnPropertyChanged(nameof(EstimatedBytesDisplay));
        OnPropertyChanged(nameof(OutputDimensionsDisplay));
    }

    private void InitializePerModeDefaults() {
        _perModeSettings[MemoryBitmapVideoMode.Vga256Color] = new VideoModeSettings { StartAddress = "A0000", Width = 320, Height = 200 };
        _perModeSettings[MemoryBitmapVideoMode.VgaModeX] = new VideoModeSettings { StartAddress = "A0000", Width = 320, Height = 240 };
        _perModeSettings[MemoryBitmapVideoMode.Ega16Color] = new VideoModeSettings { StartAddress = "A0000", Width = 640, Height = 350 };
        _perModeSettings[MemoryBitmapVideoMode.Packed4Bpp] = new VideoModeSettings { StartAddress = "A0000", Width = 320, Height = 200 };
        _perModeSettings[MemoryBitmapVideoMode.Cga4Color] = new VideoModeSettings { StartAddress = "B8000", Width = 320, Height = 200 };
        _perModeSettings[MemoryBitmapVideoMode.Cga2Color] = new VideoModeSettings { StartAddress = "B8000", Width = 640, Height = 200 };
        _perModeSettings[MemoryBitmapVideoMode.Linear1Bpp] = new VideoModeSettings { StartAddress = "A0000", Width = 640, Height = 480 };
        _perModeSettings[MemoryBitmapVideoMode.Text] = new VideoModeSettings { StartAddress = "B8000", Width = 80, Height = 25 };
        _perModeSettings[MemoryBitmapVideoMode.Raw8Bpp] = new VideoModeSettings { StartAddress = "A0000", Width = 320, Height = 200 };
    }

    private void SaveModeSettings(MemoryBitmapVideoMode mode) {
        if (!_perModeSettings.TryGetValue(mode, out VideoModeSettings? settings)) {
            settings = new VideoModeSettings();
            _perModeSettings[mode] = settings;
        }
        settings.StartAddress = StartAddress;
        settings.Width = BitmapWidth;
        settings.Height = BitmapHeight;
    }

    private void RestoreModeSettings(MemoryBitmapVideoMode mode) {
        if (_perModeSettings.TryGetValue(mode, out VideoModeSettings? settings)) {
            StartAddress = settings.StartAddress;
            BitmapWidth = settings.Width;
            BitmapHeight = settings.Height;
        }
    }

    private void UpdateHexDocument() {
        if (!TryParseHexAddress(StartAddress, out uint address)) {
            HexDocument = null;
            return;
        }
        int estimatedBytes = MemoryBitmapRenderer.EstimateRequiredBytes(SelectedVideoMode, BitmapWidth, BitmapHeight);
        if (estimatedBytes <= 0) {
            HexDocument = null;
            return;
        }
        uint endAddress = address + (uint)estimatedBytes;
        uint memLength = (uint)_memory.Length;
        if (address >= memLength) {
            HexDocument = null;
            return;
        }
        if (endAddress > memLength) {
            endAddress = memLength;
        }
        HexDocument = new DataMemoryDocument(_memory, address, endAddress);
    }

    /// <summary>
    ///     Renders the current memory region to the bitmap.
    ///     Reads raw memory and interprets using the custom MemoryBitmapRenderer.
    /// </summary>
    [RelayCommand]
    public void RenderBitmap() {
        RenderFromRawMemory();
    }

    /// <summary>
    ///     Renders a memory region using the custom MemoryBitmapRenderer.
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
        StatusText = $"Rendered {outputPixelWidth}x{outputPixelHeight} ({SelectedVideoMode}) @ {address:X5}";
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
