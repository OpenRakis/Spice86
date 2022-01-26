namespace Spice86.UI;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

using ReactiveUI;

using Spice86.Emulator.Devices.Video;
using Spice86.UI.Controls;
using Spice86.Utils;

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

[Serializable]
public class VideoBuffer : IComparable<VideoBuffer>, IDisposable {
    private uint _address;
    private int _index;

    [IgnoreDataMember]
    [JsonIgnore]
    private ScalableBitmapControl _scalableBitmapControl;

    public ScalableBitmapControl ScalableBitmapControl => _scalableBitmapControl;

    private bool _disposedValue;
    private int _width;
    private int _height;
    public uint Address => _address;

    public VideoBuffer(int width, int height, double scaleFactor, uint address, int index) {
        _width = width;
        _height = height;
        _scalableBitmapControl = new ScalableBitmapControl();
        _scalableBitmapControl.Width = width;
        _scalableBitmapControl.Height = height;
        _scalableBitmapControl.ScaleFactor = scaleFactor;
        _scalableBitmapControl.Bitmap = new WriteableBitmap(new PixelSize(640, 400), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        _address = address;
        _index = index;
    }

    public ScalableBitmapControl GetScalableControl() {
        return _scalableBitmapControl;
    }

    public int GetIndex() {
        return _index;
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
        using ILockedFramebuffer buf = _scalableBitmapControl.Bitmap.Lock();
        for (int i = 0; i < size; i++) {
            var dst = (uint*)buf.Address;
            var argb = buffer[i];
            dst[i] = argb;
        }
    }

    public int CompareTo(VideoBuffer? other) {
        if (_index < other?._index) {
            return -1;
        } else if (_index == other?._index) {
            return 0;
        } else {
            return 1;
        }
    }

    protected virtual void Dispose(bool disposing) {
        if (!_disposedValue) {
            if (disposing) {
                _scalableBitmapControl.Dispose();
            }
            _disposedValue = true;
        }
    }

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public override int GetHashCode() {
        return _index;
    }

    public override bool Equals(object? obj) {
        return this == obj || (obj is VideoBuffer other) && _index == other._index;
    }

    public override string ToString() {
        return System.Text.Json.JsonSerializer.Serialize(this);
    }
}
