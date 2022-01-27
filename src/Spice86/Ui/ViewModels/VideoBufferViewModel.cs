namespace Spice86.UI.ViewModels;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using ReactiveUI;

using Spice86.Emulator.Devices.Video;
using Spice86.Emulator.UI;

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;

public class VideoBufferViewModel : ViewModelBase, IComparable<VideoBufferViewModel>, IDisposable {
    private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
    private uint _address;

    [JsonIgnore]
    private WriteableBitmap _bitmap = default!;

    private bool _disposedValue;
    private int _height;
    private int _index;
    private UIInvalidator? _invalidator;

    private double _scalFactor = 1;

    private int _width;

    /// <summary>
    /// For AvaloniaUI Designer
    /// </summary>
    public VideoBufferViewModel() {
        if (Design.IsDesignMode == false) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
        _width = 640;
        _height = 480;
        _scalFactor = 1;
        var bitmap = new WriteableBitmap(new PixelSize(640, 400), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        Bitmap = bitmap;
        _address = 1;
        _index = 1;
    }

    public VideoBufferViewModel(MainWindowViewModel mainWindowViewModel, int width, int height, double scaleFactor, uint address, int index) {
        MainWindowViewModel = mainWindowViewModel;
        _width = width;
        _height = height;
        _scalFactor = scaleFactor;
        var bitmap = new WriteableBitmap(new PixelSize(640, 400), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        Bitmap = bitmap;
        _address = address;
        _index = index;
    }

    public uint Address => _address;

    [JsonIgnore]
    public WriteableBitmap Bitmap {
        get => _bitmap;
        set => this.RaiseAndSetIfChanged(ref _bitmap, value);
    }

    public int Height => _height;

    public UIInvalidator? Invalidator {
        get => _invalidator;
        set => _invalidator = value;
    }

    public MainWindowViewModel? MainWindowViewModel { get; private set; }

    public double ScaleFactor {
        get => _scalFactor;
        set => this.RaiseAndSetIfChanged(ref _scalFactor, value);
    }

    public int Width => _width;

    public int CompareTo(VideoBufferViewModel? other) {
        if (_index < other?._index) {
            return -1;
        } else if (_index == other?._index) {
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
        int size = _width * _height;
        long endAddress = _address + size;
        uint startAddress = _address;
        var buffer = new List<uint>(size);
        for (long i = startAddress; i < endAddress; i++) {
            byte colorIndex = memory[i];
            Rgb pixel = palette[colorIndex];
            uint argb = pixel.ToArgb();
            buffer.Add(argb);
        }
        buffer.Reverse();
        using ILockedFramebuffer? buf = Bitmap.Lock();
        for (int i = 0; i < size; i++) {
            var dst = (uint*)buf?.Address;
            var argb = buffer[i];
            dst[i] = argb;
        }

        _invalidator?.Invalidate().Wait(this._cancellation.Token);
    }

    public override bool Equals(object? obj) {
        return this == obj || (obj is VideoBufferViewModel other) && _index == other._index;
    }

    public override int GetHashCode() {
        return _index;
    }

    public int GetIndex() {
        return _index;
    }

    public override string? ToString() {
        return System.Text.Json.JsonSerializer.Serialize(this);
    }

    protected virtual void Dispose(bool disposing) {
        if (!_disposedValue) {
            if (disposing) {
                _bitmap.Dispose();
            }
            _disposedValue = true;
        }
    }
}