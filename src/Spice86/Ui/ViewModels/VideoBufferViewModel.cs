namespace Spice86.UI.ViewModels;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

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
        ScaleFactor = 1;
        Address = 1;
        _index = 1;
    }

    public VideoBufferViewModel(MainWindowViewModel mainWindowViewModel, int width, int height, double scaleFactor, uint address, int index, bool isPrimaryDisplay) {
        MainWindowViewModel = mainWindowViewModel;
        IsPrimaryDisplay = isPrimaryDisplay;
        Width = _initialWidth = width;
        Height = _initialHeight = height;
        ScaleFactor = scaleFactor;
        Address = address;
        _index = index;
        MainWindow.AppClosing += MainWindow_AppClosing;
        ScaleLessCommand = ReactiveCommand.Create(ScaleLessMethod);
        ScaleMoreCommand = ReactiveCommand.Create(ScaleMoreMethod);
        ResetCommand = ReactiveCommand.Create(ResetMethod);
    }

    private Unit ScaleLessMethod() {
        ScaleFactor--;
        return Unit.Default;
    }

    private Unit ScaleMoreMethod() {
        ScaleFactor++;
        return Unit.Default;
    }

    private Unit ResetMethod() {
        ScaleFactor = 1.7;
        return Unit.Default;
    }

    public ReactiveCommand<Unit,Unit>? ScaleMoreCommand { get; private set; }
    public ReactiveCommand<Unit,Unit>? ScaleLessCommand { get; private set; }
    public ReactiveCommand<Unit,Unit>? ResetCommand { get; private set; }

    private void MainWindow_AppClosing(object? sender, System.ComponentModel.CancelEventArgs e) {
        _appClosing = true;
    }

    public event EventHandler? Dirty;

    public uint Address { get; private set; }


    private WriteableBitmap _bitmap = new WriteableBitmap(new PixelSize(320, 200), new Vector(75, 75), PixelFormat.Bgra8888, AlphaFormat.Unpremul);

    /// <summary>
    /// DPI: AvaloniaUI, like WPF, renders UI Controls in Device Independant Pixels.<br/>
    /// According to searches online, DPI is tied to a TopLevel control (a Window).<br/>
    /// Right now, the DPI is hardcoded for WriteableBitmap : https://github.com/AvaloniaUI/Avalonia/issues/1292 <br/>
    /// See also : https://github.com/AvaloniaUI/Avalonia/pull/1889 <br/>
    /// Also WriteableBitmap is an IImage implementation and not a UI Control,<br/>
    /// that's why it's used to bind the Source property of the Image control in VideoBufferView.xaml<br/>
    /// Finally, the ViewModel is not aware of the View, so the real value is set in VideBufferView.xaml.cs.<br/>
    /// TODO: As a workaround, we must at least get the DPI from the Window in VideoBufferView.xaml.cs.<br/>
    /// </summary>
    public WriteableBitmap Bitmap {
        get => _bitmap;
        set {
            if(value is not null) {
                this.RaiseAndSetIfChanged(ref _bitmap, value);
            }
        }
    }

    private int _height = 320;

    public int Height {
        get => _height;
        private set => this.RaiseAndSetIfChanged(ref _height, value);
    }
    public bool IsPrimaryDisplay { get; private set; }
    public MainWindowViewModel? MainWindowViewModel { get; private set; }

    private double _scaleFactor = 1;
    public double ScaleFactor {
        get => _scaleFactor;
        set => this.RaiseAndSetIfChanged(ref _scaleFactor, Math.Max(value, 1.7));
    }

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
                Bitmap?.Dispose();
            }
            _disposedValue = true;
        }
    }
}