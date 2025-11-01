namespace Spice86.ViewModels;

using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator.Devices.Video;
using Spice86.ViewModels.Services;

using System.Runtime.InteropServices;

public partial class MemoryBitmapViewModel : ViewModelBase {
    private readonly IVideoState _videoState;
    private readonly IHostStorageProvider _storage;

    [ObservableProperty]
    private WriteableBitmap? _bitmap;

    [ObservableProperty]
    private byte[]? _data;

    [ObservableProperty]
    private MemoryBitmapDisplayMode _displayMode = MemoryBitmapDisplayMode.Vga8Bpp;

    [ObservableProperty]
    private bool _showOverlay;

    [ObservableProperty]
    private uint _startAddress;

    public int WidthPixels { get; set;  }

    public MemoryBitmapViewModel(IVideoState videoState, IHostStorageProvider storage) {
        _videoState = videoState;
        _storage = storage;
    }

    partial void OnDataChanged(byte[]? value) => RenderBitmap();

    partial void OnDisplayModeChanged(MemoryBitmapDisplayMode value) => RenderBitmap();

    partial void OnShowOverlayChanged(bool value) => RenderBitmap();

    [RelayCommand]
    private async Task Save() {
        if (Bitmap is not null) {
            await _storage.SaveBitmapFile(Bitmap);
        }
    }

    private void RenderBitmap() {
        if (Data is null || Data.Length == 0 || WidthPixels <= 0) {
            Bitmap = null;
            return;
        }

        switch (DisplayMode) {
            case MemoryBitmapDisplayMode.Vga8Bpp:
                BuildAsVgaEightBitsPerPixelImage();
                break;
            case MemoryBitmapDisplayMode.Cga4Color:
                BuildAsCgaFourColorImage();
                break;
            case MemoryBitmapDisplayMode.Ega16Color:
                BuildAsEgaSixteenColorImage();
                break;
            case MemoryBitmapDisplayMode.TextMode:
                BuildAsTextModeImage();
                break;
            case MemoryBitmapDisplayMode.HerculesMonochrome:
                BuildAsHerculesMonochromeImage();
                break;
        }
    }

    private void BuildAsVgaEightBitsPerPixelImage() {
        int width = WidthPixels;
        int height = Math.Max(1, (Data!.Length + width - 1) / width);

        var writeableBitmap = new WriteableBitmap(new PixelSize(width, height),
            new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

        using ILockedFramebuffer uiFrameBuffer = writeableBitmap.Lock();

        ArgbPalette palette = _videoState.DacRegisters.ArgbPalette;
        uint[] row = new uint[width];

        unsafe {
            byte* dstBase = (byte*)uiFrameBuffer.Address;
            int dstStride = uiFrameBuffer.RowBytes;

            int offset = 0;
            for (int y = 0; y < height; y++) {
                int rowCount = Math.Min(width, Data.Length - offset);
                for (int x = 0; x < rowCount; x++) {
                    row[x] = palette[Data[offset + x]];
                }
                for (int x = rowCount; x < width; x++) {
                    row[x] = 0xFF000000;
                }

                Span<byte> srcBytes = MemoryMarshal.AsBytes(row.AsSpan());
                var dst = new Span<byte>(dstBase + y * dstStride, Math.Min(srcBytes.Length, dstStride));
                srcBytes[..dst.Length].CopyTo(dst);

                offset += rowCount;
            }
        }

        Bitmap = writeableBitmap;
        
        if (ShowOverlay) {
            ApplyOverlay();
        }
    }

    private void BuildAsCgaFourColorImage() {
        // CGA 4-color mode: 2 bits per pixel, 4 colors from palette
        int width = WidthPixels;
        int pixelsPerByte = 4; // 2 bits per pixel
        int height = Math.Max(1, (Data!.Length * pixelsPerByte + width - 1) / width);

        var writeableBitmap = new WriteableBitmap(new PixelSize(width, height),
            new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

        using ILockedFramebuffer uiFrameBuffer = writeableBitmap.Lock();
        ArgbPalette palette = _videoState.DacRegisters.ArgbPalette;

        unsafe {
            byte* dstBase = (byte*)uiFrameBuffer.Address;
            int dstStride = uiFrameBuffer.RowBytes;
            uint* rowPtr = stackalloc uint[width];

            int pixelIndex = 0;
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    int byteIndex = pixelIndex / pixelsPerByte;
                    if (byteIndex < Data.Length) {
                        int bitPos = 6 - (pixelIndex % pixelsPerByte) * 2;
                        byte colorIndex = (byte)((Data[byteIndex] >> bitPos) & 0x03);
                        rowPtr[x] = palette[colorIndex];
                    } else {
                        rowPtr[x] = 0xFF000000;
                    }
                    pixelIndex++;
                }
                
                Span<byte> srcBytes = new Span<byte>(rowPtr, width * sizeof(uint));
                var dst = new Span<byte>(dstBase + y * dstStride, Math.Min(srcBytes.Length, dstStride));
                srcBytes[..dst.Length].CopyTo(dst);
            }
        }

        Bitmap = writeableBitmap;
        
        if (ShowOverlay) {
            ApplyOverlay();
        }
    }

    private void BuildAsEgaSixteenColorImage() {
        // EGA 16-color mode: 4 bits per pixel
        int width = WidthPixels;
        int pixelsPerByte = 2; // 4 bits per pixel
        int height = Math.Max(1, (Data!.Length * pixelsPerByte + width - 1) / width);

        var writeableBitmap = new WriteableBitmap(new PixelSize(width, height),
            new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

        using ILockedFramebuffer uiFrameBuffer = writeableBitmap.Lock();
        ArgbPalette palette = _videoState.DacRegisters.ArgbPalette;

        unsafe {
            byte* dstBase = (byte*)uiFrameBuffer.Address;
            int dstStride = uiFrameBuffer.RowBytes;
            uint* rowPtr = stackalloc uint[width];

            int pixelIndex = 0;
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    int byteIndex = pixelIndex / pixelsPerByte;
                    if (byteIndex < Data.Length) {
                        int bitPos = (1 - (pixelIndex % pixelsPerByte)) * 4;
                        byte colorIndex = (byte)((Data[byteIndex] >> bitPos) & 0x0F);
                        rowPtr[x] = palette[colorIndex];
                    } else {
                        rowPtr[x] = 0xFF000000;
                    }
                    pixelIndex++;
                }
                
                Span<byte> srcBytes = new Span<byte>(rowPtr, width * sizeof(uint));
                var dst = new Span<byte>(dstBase + y * dstStride, Math.Min(srcBytes.Length, dstStride));
                srcBytes[..dst.Length].CopyTo(dst);
            }
        }

        Bitmap = writeableBitmap;
        
        if (ShowOverlay) {
            ApplyOverlay();
        }
    }

    private void BuildAsTextModeImage() {
        // Text mode: Each character is 2 bytes (character code + attribute)
        // Character dimensions are typically 8x16 or 8x14
        const int charWidth = 8;
        const int charHeight = 16;
        
        int charsPerRow = WidthPixels / charWidth;
        if (charsPerRow == 0) {
            charsPerRow = 1;
        }
        
        int bytesPerChar = 2;
        int totalChars = Data!.Length / bytesPerChar;
        int rows = Math.Max(1, (totalChars + charsPerRow - 1) / charsPerRow);
        
        int width = charsPerRow * charWidth;
        int height = rows * charHeight;

        var writeableBitmap = new WriteableBitmap(new PixelSize(width, height),
            new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

        using ILockedFramebuffer uiFrameBuffer = writeableBitmap.Lock();
        ArgbPalette palette = _videoState.DacRegisters.ArgbPalette;

        unsafe {
            byte* dstBase = (byte*)uiFrameBuffer.Address;
            int dstStride = uiFrameBuffer.RowBytes;

            // For now, render as a placeholder pattern showing character codes
            // TODO: Load and render actual IBM PC fonts from video memory plane 2
            for (int charRow = 0; charRow < rows; charRow++) {
                for (int charCol = 0; charCol < charsPerRow; charCol++) {
                    int charIndex = charRow * charsPerRow + charCol;
                    int dataOffset = charIndex * bytesPerChar;
                    
                    if (dataOffset + 1 < Data.Length) {
                        byte charCode = Data[dataOffset];
                        byte attribute = Data[dataOffset + 1];
                        
                        uint fgColor = palette[attribute & 0x0F];
                        uint bgColor = palette[(attribute >> 4) & 0x0F];
                        
                        // Render a simple block pattern for now
                        for (int py = 0; py < charHeight; py++) {
                            int screenY = charRow * charHeight + py;
                            if (screenY >= height) {
                                break;
                            }
                            
                            uint* rowPtr = (uint*)(dstBase + screenY * dstStride) + charCol * charWidth;
                            
                            // Simple pattern: checkerboard based on char code
                            bool useChar = (charCode & (1 << (py % 8))) != 0;
                            
                            for (int px = 0; px < charWidth; px++) {
                                rowPtr[px] = useChar ? fgColor : bgColor;
                            }
                        }
                    }
                }
            }
        }

        Bitmap = writeableBitmap;
        
        if (ShowOverlay) {
            ApplyOverlay();
        }
    }

    private void BuildAsHerculesMonochromeImage() {
        // Hercules monochrome: 1 bit per pixel, typically 720x348
        int width = WidthPixels;
        int pixelsPerByte = 8;
        int height = Math.Max(1, (Data!.Length * pixelsPerByte + width - 1) / width);

        var writeableBitmap = new WriteableBitmap(new PixelSize(width, height),
            new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

        using ILockedFramebuffer uiFrameBuffer = writeableBitmap.Lock();

        const uint white = 0xFFFFFFFF;
        const uint black = 0xFF000000;

        unsafe {
            byte* dstBase = (byte*)uiFrameBuffer.Address;
            int dstStride = uiFrameBuffer.RowBytes;
            uint* rowPtr = stackalloc uint[width];

            int pixelIndex = 0;
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    int byteIndex = pixelIndex / pixelsPerByte;
                    if (byteIndex < Data.Length) {
                        int bitPos = 7 - (pixelIndex % pixelsPerByte);
                        bool isSet = (Data[byteIndex] & (1 << bitPos)) != 0;
                        rowPtr[x] = isSet ? white : black;
                    } else {
                        rowPtr[x] = black;
                    }
                    pixelIndex++;
                }
                
                Span<byte> srcBytes = new Span<byte>(rowPtr, width * sizeof(uint));
                var dst = new Span<byte>(dstBase + y * dstStride, Math.Min(srcBytes.Length, dstStride));
                srcBytes[..dst.Length].CopyTo(dst);
            }
        }

        Bitmap = writeableBitmap;
        
        if (ShowOverlay) {
            ApplyOverlay();
        }
    }

    private void ApplyOverlay() {
        if (Bitmap is null || Data is null) {
            return;
        }

        // TODO: Implement overlay grid showing memory addresses and values
        // This would require rendering text/lines on top of the bitmap
        // For now, this is a placeholder for future implementation
    }
}
