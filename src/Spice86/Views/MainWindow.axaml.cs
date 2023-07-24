namespace Spice86.Views;

using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;

using Spice86.Core.CLI;
using Spice86.Shared.Interfaces;
using Spice86.ViewModels;

using System.ComponentModel;

internal partial class MainWindow : Window {
    public MainWindow() {
        InitializeComponent();
    }

    private readonly Configuration? _configuration;
    private readonly ILoggerService? _loggerService;
    private readonly IClassicDesktopStyleApplicationLifetime? _desktop;

    public MainWindow(IClassicDesktopStyleApplicationLifetime desktop, Configuration configuration, ILoggerService loggerService) {
        InitializeComponent();
        _desktop = desktop;
        _configuration = configuration;
        _loggerService = loggerService;
    }

    protected override void OnOpened(EventArgs e) {
        base.OnOpened(e);
        Dispatcher.UIThread.Post(InitializeDataContext, DispatcherPriority.Background);
    }

    private void InitializeDataContext() {
        if (_desktop is null || _configuration is null || _loggerService is null) {
            return;
        }
        var mainVm = new MainWindowViewModel(_desktop, _configuration, _loggerService);
        mainVm.OnMainWindowInitialized(Image.InvalidateVisual);
        Image.PointerMoved += (s, e) => mainVm.OnMouseMoved(e, Image);
        Image.PointerPressed += (s, e) => mainVm.OnMouseButtonDown(e, Image);
        Image.PointerReleased += (s, e) => mainVm.OnMouseButtonUp(e, Image);
        Startup.SetLoggingLevel(_loggerService, _configuration);
        DataContext = mainVm;
    }
    
    protected override void OnClosed(EventArgs e) {
        (DataContext as MainWindowViewModel)?.Dispose();
        base.OnClosed(e);
    }

    private void FocusOnVideoBuffer() {
        Image.IsEnabled = false;
        Image.Focus();
        Image.IsEnabled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e) {
        (DataContext as MainWindowViewModel)?.OnKeyUp(e);
        FocusOnVideoBuffer();
    }

    protected override void OnKeyDown(KeyEventArgs e) {
        (DataContext as MainWindowViewModel)?.OnKeyDown(e);
        FocusOnVideoBuffer();
    }

    protected override void OnClosing(WindowClosingEventArgs e) {
        (DataContext as MainWindowViewModel)?.OnMainWindowClosing();
        base.OnClosing(e);
    }
}