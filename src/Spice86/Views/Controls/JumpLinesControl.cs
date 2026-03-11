namespace Spice86.Views.Controls;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using Spice86.ViewModels;

using System.Collections.Generic;

/// <summary>
/// Custom control that renders jump arc line segments for a single disassembly line.
/// Draws vertical lines, corners, and arrowheads to show jump/branch connections.
/// </summary>
public class JumpLinesControl : Control {
    private const double LaneWidth = 8;
    private const double ArrowSize = 3;
    private const double PenThickness = 1;

    private Pen? _cachedPen;
    private IBrush? _cachedLineBrush;

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
    /// The brush used to draw the jump lines.
    /// </summary>
    public static readonly StyledProperty<IBrush?> LineBrushProperty =
        AvaloniaProperty.Register<JumpLinesControl, IBrush?>(nameof(LineBrush));

    /// <summary>
    /// The brush used to draw arrowheads at jump targets.
    /// </summary>
    public static readonly StyledProperty<IBrush?> ArrowBrushProperty =
        AvaloniaProperty.Register<JumpLinesControl, IBrush?>(nameof(ArrowBrush));

    static JumpLinesControl() {
        AffectsRender<JumpLinesControl>(SegmentsProperty, MaxLanesProperty, LineBrushProperty, ArrowBrushProperty);
        AffectsMeasure<JumpLinesControl>(MaxLanesProperty);
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
    /// Gets or sets the brush used to draw the jump lines.
    /// </summary>
    public IBrush? LineBrush {
        get => GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used to draw arrowheads at jump targets.
    /// </summary>
    public IBrush? ArrowBrush {
        get => GetValue(ArrowBrushProperty);
        set => SetValue(ArrowBrushProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize) {
        double width = MaxLanes > 0 ? (MaxLanes + 1) * LaneWidth : 0;
        return new Size(width, 0);
    }

    private Pen GetOrCreatePen(IBrush brush) {
        if (_cachedPen is not null && ReferenceEquals(_cachedLineBrush, brush)) {
            return _cachedPen;
        }
        _cachedLineBrush = brush;
        _cachedPen = new Pen(brush, PenThickness);
        return _cachedPen;
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context) {
        base.Render(context);

        IReadOnlyList<JumpArcSegment>? segments = Segments;
        if (segments is not { Count: > 0 } || MaxLanes <= 0) {
            return;
        }

        IBrush lineBrush = LineBrush ?? Brushes.Gray;
        IBrush arrowBrush = ArrowBrush ?? lineBrush;
        Pen pen = GetOrCreatePen(lineBrush);
        double height = Bounds.Height;
        double width = Bounds.Width;
        double midY = height / 2;

        foreach (JumpArcSegment segment in segments) {
            double laneX = width - (segment.Lane + 1) * LaneWidth;

            switch (segment.Type) {
                case JumpSegmentType.TopEnd:
                    context.DrawLine(pen, new Point(width, midY), new Point(laneX, midY));
                    context.DrawLine(pen, new Point(laneX, midY), new Point(laneX, height));
                    if (segment.IsTarget) {
                        DrawArrow(context, arrowBrush, width, midY);
                    }
                    break;

                case JumpSegmentType.BottomEnd:
                    context.DrawLine(pen, new Point(width, midY), new Point(laneX, midY));
                    context.DrawLine(pen, new Point(laneX, 0), new Point(laneX, midY));
                    if (segment.IsTarget) {
                        DrawArrow(context, arrowBrush, width, midY);
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
