namespace Spice86.Views;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

using Spice86.Core.CLI;
using Spice86.Infrastructure;
using Spice86.Shared.Interfaces;
using Spice86.ViewModels;


internal partial class MainWindow : Window, IDisposable {
    /// <summary>
    /// IDE Designer constructor
    /// </summary>
    public MainWindow() {
        InitializeComponent();
        _uiDispatcher = new UIDispatcher(Dispatcher.UIThread);
        _uiDispatcherTimer = new UIDispatcherTimer();
        _hostStorageProvider = new HostStorageProvider(StorageProvider);
        _textClipboard = new TextClipboard(Clipboard);
        _windowActivator = new WindowActivator();
    }

    private readonly Configuration? _configuration;
    private readonly ILoggerService? _loggerService;
    private readonly IUIDispatcherTimer _uiDispatcherTimer;
    private readonly IUIDispatcher _uiDispatcher;
    private readonly ITextClipboard _textClipboard;
    private readonly IHostStorageProvider _hostStorageProvider;
    private readonly IWindowActivator _windowActivator;

    private bool _disposed;

    public MainWindow(IWindowActivator windowActivator, IUIDispatcher uiDispatcher, IUIDispatcherTimer uiDispatcherTimer, Configuration configuration, ILoggerService loggerService) {
        InitializeComponent();
        _uiDispatcherTimer = uiDispatcherTimer;
        _hostStorageProvider = new HostStorageProvider(StorageProvider);
        _textClipboard = new TextClipboard(Clipboard);
        _uiDispatcher = uiDispatcher;
        _configuration = configuration;
        _loggerService = loggerService;
        _windowActivator = windowActivator;
    }

    protected override void OnOpened(EventArgs e) {
        base.OnOpened(e);
        _uiDispatcher.Post(InitializeDataContext, DispatcherPriority.Background);
    }

    private void InitializeDataContext() {
        if (_loggerService is null || _configuration is null) {
            return;
        }
        var mainVm = new MainWindowViewModel(_windowActivator, _uiDispatcher, _hostStorageProvider, _textClipboard, _uiDispatcherTimer, _configuration, _loggerService);
        mainVm.CloseMainWindow += (_, _) => Close();
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