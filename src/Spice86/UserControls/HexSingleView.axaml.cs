namespace Spice86.UserControls;

using Avalonia;
using Avalonia.Controls;

using Spice86._3rdParty.Controls.HexView.Services;

public sealed partial class HexSingleView : UserControl, IDisposable {
    private bool _disposed;

    public HexSingleView() {
        InitializeComponent();
        _subscription = MemorySourceProperty.Changed.AddClassHandler<UserControl>(OnMemorySourceChanged);
    }

    private void OnMemorySourceChanged<TTarget>(TTarget arg1, AvaloniaPropertyChangedEventArgs arg2) where TTarget : AvaloniaObject {
        if (MemorySource is null) {
            return;
        }
        HexViewControl.LineReader = new MemoryMappedLineReader(MemorySource);
        HexViewControl.HexFormatter = new HexFormatter(MemorySource.Length);
        HexViewControl.InvalidateScrollable();
    }

    /// <summary>
    /// Defines a <see cref="StyledProperty{TValue}"/> for the <see cref="MemorySource"/> property.
    /// </summary>
    public static readonly StyledProperty<Stream?> MemorySourceProperty =
        AvaloniaProperty.Register<HexSingleView, Stream?>(nameof(MemorySource));

    private readonly IDisposable _subscription;

    /// <summary>
    /// Gets or sets the source for the hexadecimal values.
    /// </summary>
    public Stream? MemorySource {
        get { return GetValue(MemorySourceProperty); }
        set { SetValue(MemorySourceProperty, value); }
    }

    private void Dispose(bool disposing) {
        if (disposing && !_disposed) {
            _subscription.Dispose();
            _disposed = true;
        }
    }

    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}