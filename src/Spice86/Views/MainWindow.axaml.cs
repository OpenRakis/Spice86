namespace Spice86.Views;

using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;

using Spice86.Core.CLI;
using Spice86.Infrastructure;
using Spice86.Shared.Interfaces;
using Spice86.ViewModels;

using System.ComponentModel;

internal partial class MainWindow : Window, IDisposable {
    public MainWindow() {
        InitializeComponent();
    }

    private readonly Configuration? _configuration;
    private readonly ILoggerService? _loggerService;
    private readonly IClassicDesktopStyleApplicationLifetime? _desktop;
    private readonly IUIDispatcherTimer? _uiDispatcherTimer;
    private bool _disposed;

    public MainWindow(IUIDispatcherTimer uiDispatcherTimer, IClassicDesktopStyleApplicationLifetime desktop, Configuration configuration, ILoggerService loggerService) {
        InitializeComponent();
        _uiDispatcherTimer = uiDispatcherTimer;
        _desktop = desktop;
        _configuration = configuration;
        _loggerService = loggerService;
    }

    protected override void OnOpened(EventArgs e) {
        base.OnOpened(e);
        Dispatcher.UIThread.Post(InitializeDataContext, DispatcherPriority.Background);
    }

    private void InitializeDataContext() {
        if (_uiDispatcherTimer is null || _desktop is null || _configuration is null || _loggerService is null) {
            return;
        }
        var mainVm = new MainWindowViewModel(_uiDispatcherTimer, _desktop, _configuration, _loggerService);
        mainVm.OnMainWindowInitialized(Image.InvalidateVisual);
        Image.PointerMoved += (s, e) => mainVm.OnMouseMoved(e, Image);
        Image.PointerPressed += (s, e) => mainVm.OnMouseButtonDown(e, Image);
        Image.PointerReleased += (s, e) => mainVm.OnMouseButtonUp(e, Image);
        Startup.SetLoggingLevel(_loggerService, _configuration);
        DataContext = mainVm;
    }
    private void FocusOnVideoBuffer() {
        Image.IsEnabled = false;
        Image.Focus();
        Image.IsEnabled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e) {
        (DataContext as MainWindowViewModel)?.OnKeyUp(e);
        if (this.Image.IsFocused) {
            e.Handled = true;
        } else {
            FocusOnVideoBuffer();
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e) {
        (DataContext as MainWindowViewModel)?.OnKeyDown(e);
        if (this.Image.IsFocused) {
            e.Handled = true;
        } else {
            FocusOnVideoBuffer();
            e.Handled = true;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e) {
        (DataContext as MainWindowViewModel)?.OnMainWindowClosing();
        base.OnClosing(e);
    }

    protected virtual void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing && DataContext is IDisposable closedDataContext) {
                closedDataContext.Dispose();
            }
            _disposed = true;
        }
    }
    
    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}