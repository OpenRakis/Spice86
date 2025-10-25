namespace Spice86.ViewModels;

using Avalonia;
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

    public int WidthPixels { get; set;  }

    public MemoryBitmapViewModel(IVideoState videoState, IHostStorageProvider storage) {
        _videoState = videoState;
        _storage = storage;
    }

    partial void OnDataChanged(byte[]? value) => BuildAsVgaEightBitsPerPixelImage();

    [RelayCommand]
    private async Task Save() {
        if (Bitmap is not null) {
            await _storage.SaveBitmapFile(Bitmap);
        }
    }

    private void BuildAsVgaEightBitsPerPixelImage() {
        if (Data is null || Data.Length == 0 || WidthPixels <= 0) {
            Bitmap = null;
            return;
        }

        int width = WidthPixels;
        int height = Math.Max(1, (Data.Length + width - 1) / width);

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
    }
}
