namespace Spice86.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

using Spice86.Emulator.Devices.Video;
using Spice86.Utils;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json.Serialization;

/// <summary>
/// TODO: complete it!
/// </summary>

public partial class VideoBuffer : UserControl, IComparable<VideoBuffer>, ISerializable, IDisposable {
    private int _address;
    private double _scaleFactor;
    private int _index;

    [IgnoreDataMember]
    [JsonIgnore]
    private WriteableBitmap Bitmap { get; set; }

    [IgnoreDataMember]
    [JsonIgnore]
    private ScaleTransform Scale { get; set; }

    private bool _disposedValue;

    public VideoBuffer(int width, int height, double scaleFactor, int address, int index) {
        InitializeComponent();
        SetValue(WidthProperty, width);
        SetValue(HeightProperty, height);
        _scaleFactor = scaleFactor;
        _address = address;
        _index = index;
        Bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        Scale = new ScaleTransform(scaleFactor, scaleFactor);
    }

    public WriteableBitmap GetCanvas() {
        return Bitmap;
    }

    public int GetIndex() {
        return _index;
    }

    public void Draw(byte[] memory, Rgb[] palette) {
        if (Bitmap == null) {
            return;
        }

        double size = GetValue(WidthProperty) * GetValue(HeightProperty);
        //IntBuffer buffer = IntBuffer.Allocate(size);
        //int endAddress = address + size;
        //for (int i = address; i < endAddress; i++) {
        //    int colorIndex = ConvertUtils.Uint8(memory[i]);
        //    Rgb pixel = palette[colorIndex];
        //    int argb = pixel.ToArgb();
        //    buffer.Put(argb);
        //}

        //buffer.Flip();
        //Dispatcher.UIThread.InvokeAsync(() => {
        //    GraphicsContext gc = canvas.GetGraphicsContext2D();
        //    PixelWriter pw = gc.GetPixelWriter();
        //    pw.SetPixels(0, 0, width, height, PixelFormat.Bgra8888, buffer, width);
        //});
    }

    public void GetObjectData(SerializationInfo info, StreamingContext context) {
        throw new NotImplementedException();
    }

    public int CompareTo(VideoBuffer? other) {
        if (_index < other?._index) {
            return -1;
        } else if(_index == other?._index) {
            return 0;
        } 
        else {
            return 1;
        }
    }

    protected virtual void Dispose(bool disposing) {
        if (!_disposedValue) {
            if (disposing) {
                Bitmap.Dispose();
            }
            _disposedValue = true;
        }
    }

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Not allowed when inheriting from AvaloniaObject... Same for Equals.
    /// </summary>
    /// <returns></returns>
    //public override int GetHashCode() {
    //    return _index;
    //}

    public override string ToString() {
        return System.Text.Json.JsonSerializer.Serialize(this);
    }
}
