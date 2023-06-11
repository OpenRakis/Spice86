namespace Spice86.ViewModels;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Views;
using Spice86.Shared;
using Spice86.Shared.Interfaces;

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

/// <inheritdoc cref="Spice86.Shared.Interfaces.IVideoBufferViewModel" />
public sealed partial class VideoBufferViewModel : ObservableObject, IVideoBufferViewModel {
    private bool _disposedValue;

    private Thread? _drawThread;

    private bool _exitDrawThread;

    private readonly ManualResetEvent _manualResetEvent = new(false);

    /// <summary>
    /// For AvaloniaUI Designer
    /// </summary>
    public VideoBufferViewModel() {
        if (Design.IsDesignMode == false) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
        Width = 320;
        Height = 200;
        Scale = 1;
        _frameRenderTimeWatch = new Stopwatch();
    }

    public VideoBufferViewModel(IVideoCard videoCard, double scale, int width, int height) {
        _videoCard = videoCard;
        Width = width;
        Height = height;
        Scale = scale;
        MainWindow.AppClosing += MainWindow_AppClosing;
        _frameRenderTimeWatch = new Stopwatch();
        _bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
    }

    private void DrawThreadMethod() {
        while (!_exitDrawThread) {
            _drawAction?.Invoke();
            if (!_exitDrawThread) {
                _manualResetEvent.WaitOne();
            }
        }
    }

    private Action? UIUpdateMethod { get; set; }

    internal void SetUIUpdateMethod(Action invalidateImageAction) {
        UIUpdateMethod = invalidateImageAction;
    }

    [RelayCommand]
    public async Task SaveBitmap() {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            SaveFileDialog picker = new SaveFileDialog {
                DefaultExtension = "bmp",
                InitialFileName = "screenshot.bmp",
                Title = "Save Bitmap"
            };
            string? file = await picker.ShowAsync(desktop.MainWindow);
            if (string.IsNullOrWhiteSpace(file) == false) {
                Bitmap?.Save(file);
            }
        }
    }

    private void MainWindow_AppClosing(object? sender, System.ComponentModel.CancelEventArgs e) {
        _appClosing = true;
    }

    /// <summary>
    /// TODO : Get current DPI from Avalonia or Skia.
    /// It isn't DesktopScaling or RenderScaling as this returns 1 when Windows Desktop Scaling is set at 100%
    /// DPI: AvaloniaUI, like WPF, renders UI Controls in Device Independant Pixels.<br/>
    /// According to searches online, DPI is tied to a TopLevel control (a Window).<br/>
    /// Right now, the DPI is hardcoded for WriteableBitmap : https://github.com/AvaloniaUI/Avalonia/issues/1292 <br/>
    /// See also : https://github.com/AvaloniaUI/Avalonia/pull/1889 <br/>
    /// Also WriteableBitmap is an IImage implementation and not a UI Control,<br/>
    /// that's why it's used to bind the Source property of the Image control in VideoBufferView.xaml<br/>
    /// </summary>
    [ObservableProperty]
    private WriteableBitmap? _bitmap;

    private bool _showCursor = true;

    public bool ShowCursor {
        get => _showCursor;
        set {
            SetProperty(ref _showCursor, value);
            if (_showCursor) {
                Cursor?.Dispose();
                Cursor = Cursor.Default;
            } else {
                Cursor?.Dispose();
                Cursor = new Cursor(StandardCursorType.None);
            }
        }
    }

    [ObservableProperty]
    private Cursor? _cursor = Cursor.Default;

    private double _scale = 1;

    public double Scale {
        get => _scale;
        set => SetProperty(ref _scale, Math.Max(value, 1));
    }

    [ObservableProperty]
    private int _height = 200;

    [ObservableProperty]
    private int _width = 320;

    [ObservableProperty]
    private long _framesRendered;

    private bool _appClosing;

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private readonly Stopwatch _frameRenderTimeWatch;

    private Action? _drawAction;

    public void Draw() {
        if (_appClosing || _disposedValue || UIUpdateMethod is null || Bitmap is null) {
            return;
        }
        if (_drawThread is null) {
            _drawThread = new Thread(DrawThreadMethod) {
                Name = "UIRenderThread"
            };
            _drawThread.Start();
        }
        _drawAction ??= () => {
            unsafe {
                _frameRenderTimeWatch.Restart();
                using ILockedFramebuffer pixels = Bitmap.Lock();
                var buffer = new Span<uint>((void*)pixels.Address, pixels.RowBytes * pixels.Size.Height / 4);
                // _videoCard?.Render(Address, Width, Height, pixels.Address);
                _videoCard?.Render(buffer);

                Dispatcher.UIThread.Post(() => {
                    UIUpdateMethod?.Invoke();
                    FramesRendered++;
                }, DispatcherPriority.Render);
                _frameRenderTimeWatch.Stop();
                LastFrameRenderTimeMs = _frameRenderTimeWatch.ElapsedMilliseconds;
            }
        };
        if (!_exitDrawThread) {
            _manualResetEvent.Set();
            _manualResetEvent.Reset();
        }
    }

    [ObservableProperty]
    private long _lastFrameRenderTimeMs;

    private readonly IVideoCard? _videoCard;

    private void Dispose(bool disposing) {
        if (!_disposedValue) {
            if (disposing) {
                _exitDrawThread = true;
                _manualResetEvent.Set();
                if (_drawThread?.IsAlive == true) {
                    _drawThread.Join();
                }
                _manualResetEvent.Dispose();
                Dispatcher.UIThread.Post(() => {
                    Bitmap?.Dispose();
                    Bitmap = null;
                    Cursor?.Dispose();
                    UIUpdateMethod?.Invoke();
                }, DispatcherPriority.MaxValue);
            }
            _disposedValue = true;
        }
    }
}