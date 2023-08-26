namespace Spice86;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using Spice86.Shared.Interfaces;
using Spice86.Views;
using Spice86.Core.CLI;

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

    public void SetupMainWindow(IClassicDesktopStyleApplicationLifetime desktop, Configuration configuration, ILoggerService loggerService) {
        var mainWindow = new MainWindow(desktop, configuration, loggerService);
        _mainWindow = mainWindow;
        desktop.MainWindow = mainWindow;
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