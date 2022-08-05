namespace Spice86.UI.ViewModels;

using Microsoft.Win32;

using Prism.Commands;

using ReactiveUI;

using Spice86.Shared;
using Spice86.Shared.Interfaces;
using Spice86.WPF;
using Spice86.WPF.CustomControls;
using Spice86.WPF.Views;

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

public partial class WPFVideoBufferViewModel : ReactiveObject, IComparable<WPFVideoBufferViewModel>, IVideoBufferViewModel, IDisposable {
    private bool _disposedValue;

    public DelegateCommand SaveBitmapCommand;
    private Stopwatch _frameRenderTimeWatch;

    /// <summary>
    /// For AvaloniaUI Designer
    /// </summary>
    public WPFVideoBufferViewModel() {
        if (!DesignerProperties.GetIsInDesignMode(Application.Current.MainWindow)) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
        Width = 320;
        Height = 200;
        Address = 1;
        _index = 1;
        Scale = 1;
        _renderTarget = new FastBitmap(Width, Height);
        SaveBitmapCommand = new(SaveBitmap);
        _frameRenderTimeWatch = new Stopwatch();
    }

    public WPFVideoBufferViewModel(double scale, int width, int height, uint address, int index, bool isPrimaryDisplay) {
        _isPrimaryDisplay = isPrimaryDisplay;
        Width = width;
        Height = height;
        Address = address;
        _index = index;
        Scale = scale;
        MainWindow.AppClosing += MainWindow_AppClosing;
        _renderTarget = new FastBitmap(Width, Height);
        SaveBitmapCommand = new(SaveBitmap);
        _frameRenderTimeWatch = new Stopwatch();
    }

    private FastBitmap _renderTarget;

    public int Width {
        get => _width;
        set => this.RaiseAndSetIfChanged(ref _width, value);
    }

    public int Height {
        get => _height;
        set => this.RaiseAndSetIfChanged(ref _height, value);
    }

    private long _framesRendered = 0;

    public long FramesRendered {
        get => _framesRendered;
        set => this.RaiseAndSetIfChanged(ref _framesRendered, value);
    }

    public void SaveBitmap() {
        var picker = new SaveFileDialog {
            DefaultExt = "bmp",
            Title = "Save Bitmap",
            CheckPathExists = true
        };
        if(picker.ShowDialog(Application.Current.MainWindow) == true) {
            if (string.IsNullOrWhiteSpace(picker.FileName) == false) {
                SaveBitmapToFile(picker.FileName, this.Bitmap);
            }
        }
    }

    private static void SaveBitmapToFile(string filename, BitmapSource? source) {
        if (filename != string.Empty && source is not null) {
            using FileStream stream = new FileStream(filename, FileMode.Create);
            BmpBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(source));
            encoder.Save(stream);
        }
    }

    private void MainWindow_AppClosing(object? sender, CancelEventArgs e) {
        _appClosing = true;
    }

    public uint Address { get; private set; }

    public BitmapSource? Bitmap {
        get => _renderTarget.InteropBitmap;
    }

    private bool _showCursor = true;

    public bool ShowCursor {
        get => _showCursor;
        set {
            this.RaiseAndSetIfChanged(ref _showCursor, value);
            if (_showCursor) {
                Cursor?.Dispose();
                Cursor = Cursors.Arrow;
            } else {
                Cursor?.Dispose();
                Cursor = Cursors.None;
            }
        }
    }

    private Cursor? _cursor = Cursors.Arrow;

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

    private bool _isPrimaryDisplay;

    public bool IsPrimaryDisplay {
        get => _isPrimaryDisplay;
        private set => this.RaiseAndSetIfChanged(ref _isPrimaryDisplay, value);
    }

    private int _width = 200;

    private bool _appClosing;

    private readonly int _index;

    public int CompareTo(WPFVideoBufferViewModel? other) {
        if (_index < other?._index) {
            return -1;
        }
        if (_index == other?._index) {
            return 0;
        }
        return 1;
    }

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private bool _isDrawing;

    public bool IsDrawing {
        get => _isDrawing;
        set => this.RaiseAndSetIfChanged(ref _isDrawing, value);
    }

    public unsafe void Draw(byte[] memory, Rgb[] palette) {
        if (_appClosing || _disposedValue || Bitmap is null) {
            return;
        }

        _frameRenderTimeWatch.Restart();

        uint totalPixels = (uint)Width * (uint)Height;
        uint startAddress = Address;
        long endAddress = Address + totalPixels;
        IntPtr destination = this._renderTarget.PixelBuffer;

        uint* destPtr = (uint*)destination.ToPointer();

        int offset = 0;
        for (int y = 0; y < Height; y++) {
            uint* startPtr = destPtr + offset;
            uint* endPtr = destPtr + offset + Width;
            for (uint* x = startPtr; x < endPtr; x++) {
                byte src = memory[startAddress + offset];
                Rgb? pixel = palette[src];
                *x = pixel.ToArgb();
                offset++;
            }
        }

        App.Current.Dispatcher.Invoke(() => {
            _renderTarget.InteropBitmap?.Invalidate();
            FramesRendered++;
        }, DispatcherPriority.Render);

        _frameRenderTimeWatch.Stop();
        LastFrameRenderTime = _frameRenderTimeWatch.ElapsedMilliseconds;
    }

    private long _lastFrameRenderTimeMs;

    public long LastFrameRenderTime {
        get => _lastFrameRenderTimeMs;
        set => this.RaiseAndSetIfChanged(ref _lastFrameRenderTimeMs, value);
    }

    public override bool Equals(object? obj) {
        return this == obj || (obj is WPFVideoBufferViewModel other) && _index == other._index;
    }

    public override int GetHashCode() {
        return _index;
    }

    protected virtual void Dispose(bool disposing) {
        if (!_disposedValue) {
            if (disposing) {
                App.Current.Dispatcher.Invoke(() => {
                    Cursor?.Dispose();
                    _renderTarget.Dispose();
                }, DispatcherPriority.Render);
            }
            _disposedValue = true;
        }
    }
}