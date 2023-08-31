namespace Spice86;

using Avalonia;
using Avalonia.Markup.Xaml;

using Spice86.Shared.Interfaces;
using Spice86.Views;
using Spice86.Core.CLI;
using Spice86.Infrastructure;

/// <summary>
/// The main entry point for the Spice86 UI.
/// </summary>
internal partial class App : Application, IDisposable {
    private bool _disposed;

    private IDisposable? _mainWindow;

    /// <summary>
    /// Initializes the Spice86 UI.
    /// </summary>
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public MainWindow CreateMainWindow(IUIDispatcher uiDispatcher, IUIDispatcherTimer uiDispatcherTimer, Configuration configuration, ILoggerService loggerService) {
        var mainWindow = new MainWindow(uiDispatcher, uiDispatcherTimer, configuration, loggerService);
        _mainWindow = mainWindow;
        return mainWindow;
    }

    protected virtual void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing && _mainWindow is IDisposable closedMainWindow) {
                closedMainWindow.Dispose();
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