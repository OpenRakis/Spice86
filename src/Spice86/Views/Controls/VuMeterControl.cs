namespace Spice86.Views.Controls;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

/// <summary>
/// A classic VU meter control that displays audio levels as segmented vertical bars.
/// Styled after the Windows 95 mixer with green/yellow/red color zones.
/// </summary>
public sealed class VuMeterControl : Control {
    private const int SegmentCount = 20;
    private const double SegmentGap = 1.0;

    /// <summary>
    /// Defines the Level property (0.0 to 1.0).
    /// </summary>
    public static readonly StyledProperty<double> LevelProperty =
        AvaloniaProperty.Register<VuMeterControl, double>(nameof(Level), 0.0);

    /// <summary>
    /// Gets or sets the current level (0.0 to 1.0).
    /// </summary>
    public double Level {
        get => GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    static VuMeterControl() {
        AffectsRender<VuMeterControl>(LevelProperty);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VuMeterControl"/> class.
    /// </summary>
    public VuMeterControl() {
        Width = 12;
        MinHeight = 100;
    }

    /// <summary>
    /// Renders the VU meter with segmented blocks.
    /// </summary>
    public override void Render(DrawingContext context) {
        base.Render(context);

        double width = Bounds.Width;
        double height = Bounds.Height;

        if (width <= 0 || height <= 0) {
            return;
        }

        // Calculate segment dimensions
        double totalGapSpace = SegmentGap * (SegmentCount - 1);
        double segmentHeight = (height - totalGapSpace) / SegmentCount;

        if (segmentHeight <= 0) {
            return;
        }

        // Calculate how many segments are lit based on level
        int litSegments = (int)(Level * SegmentCount);
        litSegments = Math.Clamp(litSegments, 0, SegmentCount);

        // Draw segments from bottom to top
        for (int i = 0; i < SegmentCount; i++) {
            // Y position: segment 0 is at the bottom
            double y = height - (i + 1) * segmentHeight - i * SegmentGap;

            // Determine color based on position in the meter
            // Bottom 60% = green, next 25% = yellow, top 15% = red
            IBrush fillBrush;
            IBrush dimBrush;

            double segmentPosition = (double)i / SegmentCount;

            if (segmentPosition >= 0.85) {
                // Top 15% - Red zone (high/clipping)
                fillBrush = new SolidColorBrush(Color.FromRgb(220, 50, 50));
                dimBrush = new SolidColorBrush(Color.FromRgb(80, 20, 20));
            } else if (segmentPosition >= 0.60) {
                // Middle 25% - Yellow zone (medium)
                fillBrush = new SolidColorBrush(Color.FromRgb(220, 200, 50));
                dimBrush = new SolidColorBrush(Color.FromRgb(80, 70, 20));
            } else {
                // Bottom 60% - Green zone (low/normal)
                fillBrush = new SolidColorBrush(Color.FromRgb(50, 200, 50));
                dimBrush = new SolidColorBrush(Color.FromRgb(20, 70, 20));
            }

            // Use lit or dim color based on current level
            IBrush brush = i < litSegments ? fillBrush : dimBrush;

            Rect segmentRect = new Rect(0, y, width, segmentHeight);
            context.FillRectangle(brush, segmentRect);
        }
    }
}
