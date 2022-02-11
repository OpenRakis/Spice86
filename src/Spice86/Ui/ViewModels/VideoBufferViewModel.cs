namespace Spice86.UI.ViewModels;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

using ReactiveUI;

using Spice86.Emulator.Devices.Video;
using Spice86.UI.Views;

using System;
using System.Reactive;

public class VideoBufferViewModel : ViewModelBase, IComparable<VideoBufferViewModel>, IDisposable {
    private bool _disposedValue;
    private int _initialHeight;
    private int _initialWidth;

    /// <summary>
    /// For AvaloniaUI Designer
    /// </summary>
    public VideoBufferViewModel() {
        if (Design.IsDesignMode == false) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
        Width = 320;
        Height = 200;
        Address = 1;
        _index = 1;
        Scale = 1;
    }

    public VideoBufferViewModel(MainWindowViewModel mainWindowViewModel, double scale, int width, int height, uint address, int index, bool isPrimaryDisplay) {
        MainWindowViewModel = mainWindowViewModel;
        IsPrimaryDisplay = isPrimaryDisplay;
        Width = _initialWidth = width;
        Height = _initialHeight = height;
        Address = address;
        _index = index;
        Scale = scale;
        MainWindow.AppClosing += MainWindow_AppClosing;
    }

    private void MainWindow_AppClosing(object? sender, System.ComponentModel.CancelEventArgs e) {
        _appClosing = true;
    }

    public event EventHandler? Dirty;

    public uint Address { get; private set; }


    // TODO : Get current DPI from Avalonia or Skia.
    // It isn't DesktopScaling or RenderScaling as this returns 1 when Windows Desktop Scaling is set at 100%
    private WriteableBitmap _bitmap = new WriteableBitmap(new PixelSize(320, 200), new Vector(75, 75), PixelFormat.Bgra8888, AlphaFormat.Unpremul);

    /// <summary>
    /// DPI: AvaloniaUI, like WPF, renders UI Controls in Device Independant Pixels.<br/>
    /// According to searches online, DPI is tied to a TopLevel control (a Window).<br/>
    /// Right now, the DPI is hardcoded for WriteableBitmap : https://github.com/AvaloniaUI/Avalonia/issues/1292 <br/>
    /// See also : https://github.com/AvaloniaUI/Avalonia/pull/1889 <br/>
    /// Also WriteableBitmap is an IImage implementation and not a UI Control,<br/>
    /// that's why it's used to bind the Source property of the Image control in VideoBufferView.xaml<br/>
    /// </summary>
    public WriteableBitmap Bitmap {
        get => _bitmap;
        set {
            if(value is not null) {
                this.RaiseAndSetIfChanged(ref _bitmap, value);
            }
        }
    }

    private bool _showCursor = true;

    public bool ShowCursor {
        get => _showCursor;
        set {
            this.RaiseAndSetIfChanged(ref _showCursor, value);
            if (_showCursor) {
                Cursor?.Dispose();
                Cursor = Cursor.Default;
            }
            else {
                Cursor?.Dispose();
                Cursor = new Cursor(StandardCursorType.None);
            }
        }
    }

    private Cursor? _cursor = Cursor.Default;

    public Cursor? Cursor {
        get => _cursor;
        set => this.RaiseAndSetIfChanged(ref _cursor, value);
    }

    private double _scale = 1;

    public double Scale {
        get => _scale;
        set => this.RaiseAndSetIfChanged(ref _scale, Math.Max(value, 1));
    }

    private int _height = 320;

    public int Height {
        get => _height;
        private set => this.RaiseAndSetIfChanged(ref _height, value);
    }
    public bool IsPrimaryDisplay { get; private set; }
    public MainWindowViewModel? MainWindowViewModel { get; private set; }

    private int _width = 200;
    private bool _appClosing;

    public int Width {
        get => _width;
        private set => this.RaiseAndSetIfChanged(ref _width, value);
    }

    private int _index;

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
        if (_disposedValue || Dirty is null || Bitmap is null) {
            return;
        }
        int size = Width * Height;
        long endAddress = Address + size;
        
        if(_appClosing == false) {
            using ILockedFramebuffer buf = Bitmap.Lock();
            uint* dst = (uint*)buf.Address;
            for (long i = Address; i < endAddress; i++) {
                byte colorIndex = memory[i];
                Rgb pixel = palette[colorIndex];
                uint argb = pixel.ToArgb();
                dst[i-Address] = argb;
            }
            Dirty.Invoke(this, EventArgs.Empty);
        }
    }

    public override bool Equals(object? obj) {
        return this == obj || (obj is VideoBufferViewModel other) && _index == other._index;
    }

    public override int GetHashCode() {
        return _index;
    }

    protected virtual void Dispose(bool disposing) {
        if (!_disposedValue) {
            if (disposing) {
                Dispatcher.UIThread.Post(() => {
                    Bitmap?.Dispose();
                    Cursor?.Dispose();
                }, DispatcherPriority.MaxValue);
            }
            _disposedValue = true;
        }
    }
}