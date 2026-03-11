namespace Spice86.Views.Controls;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

using Spice86.ViewModels;

using System.Collections.Generic;

/// <summary>
/// Custom control that renders jump arc line segments for a single disassembly line.
/// Draws vertical lines, corners, and arrowheads to show jump/branch connections.
/// Each arc is colored from a palette of theme-aware brushes (DisassemblyJumpLine0Brush..7Brush).
/// </summary>
public class JumpLinesControl : Control {
    private const double LaneWidth = 8;
    private const double ArrowSize = 3;
    private const double PenThickness = 1;
    private const int PaletteSize = 8;
    private const string BrushKeyPrefix = "DisassemblyJumpLine";
    private const string BrushKeySuffix = "Brush";

    private readonly IBrush?[] _paletteBrushes = new IBrush?[PaletteSize];
    private readonly Pen?[] _cachedPens = new Pen?[PaletteSize];
    private bool _paletteResolved;

    private static readonly StreamGeometry ArrowGeometry = CreateArrowGeometry();

    /// <summary>
    /// The list of jump arc segments passing through this line.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<JumpArcSegment>?> SegmentsProperty =
        AvaloniaProperty.Register<JumpLinesControl, IReadOnlyList<JumpArcSegment>?>(nameof(Segments));

    /// <summary>
    /// The maximum number of lanes across all visible lines (determines control width).
    /// </summary>
    public static readonly StyledProperty<int> MaxLanesProperty =
        AvaloniaProperty.Register<JumpLinesControl, int>(nameof(MaxLanes));

    /// <summary>
    /// Fallback brush used when no palette brush is found for a given color index.
    /// </summary>
    public static readonly StyledProperty<IBrush?> LineBrushProperty =
        AvaloniaProperty.Register<JumpLinesControl, IBrush?>(nameof(LineBrush));

    static JumpLinesControl() {
        AffectsRender<JumpLinesControl>(SegmentsProperty, MaxLanesProperty, LineBrushProperty);
        AffectsMeasure<JumpLinesControl>(MaxLanesProperty);
        LineBrushProperty.Changed.AddClassHandler<JumpLinesControl>((control, _) => control.InvalidatePaletteCache());
    }

    /// <summary>
    /// Gets or sets the list of jump arc segments passing through this line.
    /// </summary>
    public IReadOnlyList<JumpArcSegment>? Segments {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum number of lanes across all visible lines.
    /// </summary>
    public int MaxLanes {
        get => GetValue(MaxLanesProperty);
        set => SetValue(MaxLanesProperty, value);
    }

    /// <summary>
    /// Gets or sets the fallback brush used when no palette brush is found.
    /// </summary>
    public IBrush? LineBrush {
        get => GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize) {
        double width = MaxLanes > 0 ? (MaxLanes + 1) * LaneWidth : 0;
        return new Size(width, 0);
    }

    private void InvalidatePaletteCache() {
        _paletteResolved = false;
        Array.Clear(_paletteBrushes);
        Array.Clear(_cachedPens);
    }

    private void ResolvePalette() {
        if (_paletteResolved) {
            return;
        }
        for (int i = 0; i < PaletteSize; i++) {
            string key = $"{BrushKeyPrefix}{i}{BrushKeySuffix}";
            if (this.TryFindResource(key, ActualThemeVariant, out object? resource) && resource is IBrush brush) {
                _paletteBrushes[i] = brush;
            }
        }
        _paletteResolved = true;
    }

    private IBrush GetBrushForSegment(JumpArcSegment segment, IBrush fallback) {
        int slot = segment.ColorIndex % PaletteSize;
        return _paletteBrushes[slot] ?? fallback;
    }

    private Pen GetOrCreatePen(int colorIndex, IBrush brush) {
        int slot = colorIndex % PaletteSize;
        if (_cachedPens[slot] is { } cached && ReferenceEquals(_paletteBrushes[slot], brush)) {
            return cached;
        }
        Pen pen = new(brush, PenThickness);
        _cachedPens[slot] = pen;
        return pen;
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context) {
        base.Render(context);

        IReadOnlyList<JumpArcSegment>? segments = Segments;
        if (segments is not { Count: > 0 } || MaxLanes <= 0) {
            return;
        }

        ResolvePalette();
        IBrush fallbackBrush = LineBrush ?? Brushes.Gray;
        double height = Bounds.Height;
        double width = Bounds.Width;
        double midY = height / 2;

        foreach (JumpArcSegment segment in segments) {
            IBrush segmentBrush = GetBrushForSegment(segment, fallbackBrush);
            Pen pen = GetOrCreatePen(segment.ColorIndex, segmentBrush);
            double laneX = width - (segment.Lane + 1) * LaneWidth;

            switch (segment.Type) {
                case JumpSegmentType.TopEnd:
                    context.DrawLine(pen, new Point(width, midY), new Point(laneX, midY));
                    context.DrawLine(pen, new Point(laneX, midY), new Point(laneX, height));
                    if (segment.IsTarget) {
                        DrawArrow(context, segmentBrush, width, midY);
                    }
                    break;

                case JumpSegmentType.BottomEnd:
                    context.DrawLine(pen, new Point(width, midY), new Point(laneX, midY));
                    context.DrawLine(pen, new Point(laneX, 0), new Point(laneX, midY));
                    if (segment.IsTarget) {
                        DrawArrow(context, segmentBrush, width, midY);
                    }
                    break;

                case JumpSegmentType.Middle:
                    context.DrawLine(pen, new Point(laneX, 0), new Point(laneX, height));
                    break;
            }
        }
    }

    /// <summary>
    /// Creates an arrow geometry centered at origin, pointing right. Tip at (0,0).
    /// </summary>
    private static StreamGeometry CreateArrowGeometry() {
        StreamGeometry geometry = new();
        using (StreamGeometryContext ctx = geometry.Open()) {
            ctx.BeginFigure(new Point(0, 0), true);
            ctx.LineTo(new Point(-ArrowSize * 2, -ArrowSize));
            ctx.LineTo(new Point(-ArrowSize * 2, ArrowSize));
            ctx.EndFigure(true);
        }
        return geometry;
    }

    /// <summary>
    /// Draws the cached right-pointing arrowhead translated to the specified tip position.
    /// </summary>
    private static void DrawArrow(DrawingContext context, IBrush brush, double tipX, double tipY) {
        using DrawingContext.PushedState _ = context.PushTransform(Matrix.CreateTranslation(tipX, tipY));
        context.DrawGeometry(brush, null, ArrowGeometry);
    }
}
