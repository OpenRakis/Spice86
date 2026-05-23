namespace Spice86.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

using Spice86.ViewModels;
using Spice86.ViewModels.DataModels;
using Spice86.Views.UserControls;

/// <summary>Code-behind for the combined DOS memory overview view.</summary>
public partial class DosMemoryOverviewView : EmulatorObjectUserControl {
    private static readonly IBrush FreeBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
    private static readonly IBrush UsedBrush = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));

    private DosMemoryOverviewViewModel? _vm;
    private Canvas? _barCanvas;

    /// <summary>Initializes a new <see cref="DosMemoryOverviewView"/>.</summary>
    public DosMemoryOverviewView() {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
        base.OnAttachedToVisualTree(e);
        _barCanvas = this.FindControl<Canvas>("BarCanvas");
        if (_barCanvas is not null) {
            _barCanvas.SizeChanged += OnBarCanvasSizeChanged;
        }
        RebuildBars();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
        if (_barCanvas is not null) {
            _barCanvas.SizeChanged -= OnBarCanvasSizeChanged;
        }
        if (_vm is not null) {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm = null;
        }
        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e) {
        base.OnDataContextChanged(e);
        if (_vm is not null) {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm.IsVisible = false;
        }
        _vm = DataContext as DosMemoryOverviewViewModel;
        if (_vm is not null) {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
            _vm.IsVisible = IsViewModelEffectivelyVisible;
            RebuildBars();
        }
    }

    private void OnBarCanvasSizeChanged(object? sender, SizeChangedEventArgs e) {
        RebuildBars();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(DosMemoryOverviewViewModel.SegmentsVersion)) {
            RebuildBars();
        }
    }

    private void RebuildBars() {
        if (_barCanvas is null || _vm is null) {
            return;
        }

        _barCanvas.Children.Clear();

        double width = _barCanvas.Bounds.Width;
        double height = _barCanvas.Bounds.Height;
        if (width <= 1 || height <= 1) {
            return;
        }

        IReadOnlyList<ConventionalMemorySegment> segments = _vm.Segments;
        if (segments.Count == 0) {
            return;
        }

        long maxSize = 0;
        foreach (ConventionalMemorySegment seg in segments) {
            if (seg.SizeBytes > maxSize) {
                maxSize = seg.SizeBytes;
            }
        }
        if (maxSize <= 0) {
            return;
        }

        const double labelArea = 18;
        double plotHeight = height - labelArea;
        if (plotHeight < 4) {
            plotHeight = height;
        }

        double gap = 2;
        double barWidth = (width - gap * (segments.Count + 1)) / segments.Count;
        if (barWidth < 1) {
            barWidth = 1;
            gap = 0;
        }

        for (int i = 0; i < segments.Count; i++) {
            ConventionalMemorySegment seg = segments[i];
            double ratio = (double)seg.SizeBytes / maxSize;
            double barHeight = ratio * plotHeight;
            if (barHeight < 1) {
                barHeight = 1;
            }

            IBrush fill;
            if (seg.IsFree) {
                fill = FreeBrush;
            } else {
                fill = UsedBrush;
            }

            Rectangle bar = new() {
                Width = barWidth,
                Height = barHeight,
                Fill = fill,
                Stroke = Brushes.Black,
                StrokeThickness = 0.5
            };
            string tip = $"Seg 0x{seg.StartSegment:X4}  /  {seg.SizeBytes:N0} bytes  /  {seg.Owner}";
            ToolTip.SetTip(bar, tip);
            double x = gap + i * (barWidth + gap);
            double y = plotHeight - barHeight;
            Canvas.SetLeft(bar, x);
            Canvas.SetTop(bar, y);
            _barCanvas.Children.Add(bar);
        }

        // Baseline
        Rectangle baseline = new() {
            Width = width,
            Height = 1,
            Fill = Brushes.Gray
        };
        Canvas.SetLeft(baseline, 0);
        Canvas.SetTop(baseline, plotHeight);
        _barCanvas.Children.Add(baseline);
    }
}
