namespace Spice86.UI.ViewModels;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using Spice86.Emulator.Devices.Video;

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

public class VideoBufferViewModel : ViewModelBase, IComparable<VideoBufferViewModel>, IDisposable {
    private bool _disposedValue;

    /// <summary>
    /// For AvaloniaUI Designer
    /// </summary>
    public VideoBufferViewModel() {
        if (Design.IsDesignMode == false) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
        Width = 640;
        Height = 480;
        ScaleFactor = 1;
        var bitmap = new WriteableBitmap(new PixelSize(320, 200), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        Bitmap = bitmap;
        Address = 1;
        Index = 1;
    }

    public VideoBufferViewModel(MainWindowViewModel mainWindowViewModel, int width, int height, double scaleFactor, uint address, int index, bool isPrimaryDisplay) {
        MainWindowViewModel = mainWindowViewModel;
        IsPrimaryDisplay = isPrimaryDisplay;
        Width = width;
        Height = height;
        ScaleFactor = scaleFactor;
        // Todo: compute the dpi parameter from the parent window size?
        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(75, 75), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        Bitmap = bitmap;
        Address = address;
        Index = index;
    }

    public static event EventHandler? IsDirty;

    public uint Address { get; private set; }

    [JsonIgnore]
    public WriteableBitmap Bitmap { get; private set; }

    public int Height { get; private set; }
    public bool IsPrimaryDisplay { get; private set; }
    public MainWindowViewModel? MainWindowViewModel { get; private set; }
    public double ScaleFactor { get; private set; }
    public int Width { get; private set; }
    private int Index { get; set; }

    public int CompareTo(VideoBufferViewModel? other) {
        if (Index < other?.Index) {
            return -1;
        } else if (Index == other?.Index) {
            return 0;
        } else {
            return 1;
        }
    }

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public unsafe void Draw(byte[] memory, Rgb[] palette) {
        int size = Width * Height;
        long endAddress = Address + size;
        var buffer = new List<uint>(size);
        for (long i = Address; i < endAddress; i++) {
            byte colorIndex = memory[i];
            Rgb pixel = palette[colorIndex];
            uint argb = pixel.ToArgb();
            buffer.Add(argb);
        }
        if (_disposedValue == false) {
            using ILockedFramebuffer buf = Bitmap.Lock();
            uint* dst = (uint*)buf.Address;
            for (int i = 0; i < size; i++) {
                uint argb = buffer[i];
                dst[i] = argb;
            }
            IsDirty?.Invoke(this, EventArgs.Empty);
        }
    }

    public override bool Equals(object? obj) {
        return this == obj || (obj is VideoBufferViewModel other) && Index == other.Index;
    }

    public override int GetHashCode() {
        return Index;
    }

    public int GetIndex() {
        return Index;
    }

    public override string? ToString() {
        return System.Text.Json.JsonSerializer.Serialize(this);
    }

    protected virtual void Dispose(bool disposing) {
        if (!_disposedValue) {
            if (disposing) {
                Bitmap.Dispose();
            }
            _disposedValue = true;
        }
    }
}