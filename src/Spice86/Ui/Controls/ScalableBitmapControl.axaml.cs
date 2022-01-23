namespace Spice86.UI.Controls;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using System;

public partial class ScalableBitmapControl : UserControl, IDisposable {

    private bool _disposedValue;

    public ScalableBitmapControl() {
        InitializeComponent();

    }

    public double ScaleFactor {
        get => GetValue(ScaleFactorProperty);
        set {
            if (GetValue(ScaleFactorProperty) != value) {
                SetValue(ScaleFactorProperty, value);
            }
        }
    }

    public static StyledProperty<double> ScaleFactorProperty =
        AvaloniaProperty.Register<ScalableBitmapControl, double>(nameof(ScaleFactor), 1);

    public WriteableBitmap Bitmap {
        get => GetValue(BitmapProperty);
        set {
            if (GetValue(BitmapProperty) != value) {
                SetValue(BitmapProperty, value);
            }
        }
    }

    public static StyledProperty<WriteableBitmap> BitmapProperty =
        AvaloniaProperty.Register<ScalableBitmapControl, WriteableBitmap>(nameof(BitmapProperty),
            new WriteableBitmap(new PixelSize(640,400), new Vector(96,96), PixelFormat.Rgba8888, AlphaFormat.Unpremul));

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
}
