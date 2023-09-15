namespace Spice86.Views;

using Avalonia.Controls;

using Spice86.ViewModels;

public sealed partial class DebugWindow : Window, IDisposable {
    private bool _disposed;
    public DebugWindow() {
        InitializeComponent();
        Closed += (_, _) => Dispose();
    }

    private void Dispose(bool disposing) {
        if (disposing && !_disposed) {
            HexSingleView.Dispose();
            _disposed = true;
        }
    }

    protected override void OnDataContextChanged(EventArgs e) {
        base.OnDataContextChanged(e);
        ((DebugViewModel?)DataContext)?.StartObserverTimer();
    }

    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}