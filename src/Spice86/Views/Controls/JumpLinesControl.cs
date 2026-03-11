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

    /// <inheritdoc />
    public override void Render(DrawingContext context) {
        base.Render(context);

        IReadOnlyList<JumpArcSegment>? segments = Segments;
        if (segments is not { Count: > 0 } || MaxLanes <= 0) {
            return;
        }

        IBrush lineBrush = LineBrush ?? Brushes.Gray;
        IBrush arrowBrush = ArrowBrush ?? lineBrush;
        Pen pen = new(lineBrush, PenThickness);
        double height = Bounds.Height;
        double width = Bounds.Width;
        double midY = height / 2;

        foreach (JumpArcSegment segment in segments) {
            double laneX = width - (segment.Lane + 1) * LaneWidth;

            switch (segment.Type) {
                case JumpSegmentType.TopEnd:
                    // Horizontal line from right edge to lane position
                    context.DrawLine(pen, new Point(width, midY), new Point(laneX, midY));
                    // Vertical line from midpoint down to bottom edge
                    context.DrawLine(pen, new Point(laneX, midY), new Point(laneX, height));
                    if (segment.IsTarget) {
                        DrawArrow(context, arrowBrush, width, midY);
                    }
                    break;

                case JumpSegmentType.BottomEnd:
                    // Horizontal line from right edge to lane position
                    context.DrawLine(pen, new Point(width, midY), new Point(laneX, midY));
                    // Vertical line from top edge down to midpoint
                    context.DrawLine(pen, new Point(laneX, 0), new Point(laneX, midY));
                    if (segment.IsTarget) {
                        DrawArrow(context, arrowBrush, width, midY);
                    }
                    break;

                case JumpSegmentType.Middle:
                    // Vertical line spanning full height
                    context.DrawLine(pen, new Point(laneX, 0), new Point(laneX, height));
                    break;
            }
        }
    }

    /// <summary>
    /// Draws a small right-pointing arrowhead at the specified position.
    /// </summary>
    private static void DrawArrow(DrawingContext context, IBrush brush, double tipX, double tipY) {
        StreamGeometry arrow = new();
        using (StreamGeometryContext arrowCtx = arrow.Open()) {
            arrowCtx.BeginFigure(new Point(tipX, tipY), true);
            arrowCtx.LineTo(new Point(tipX - ArrowSize * 2, tipY - ArrowSize));
            arrowCtx.LineTo(new Point(tipX - ArrowSize * 2, tipY + ArrowSize));
            arrowCtx.EndFigure(true);
        }
        context.DrawGeometry(brush, null, arrow);
    }
}
