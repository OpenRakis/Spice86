using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;

namespace AvaloniaGraphControl
{
  class RoundedRectangleGeometry : PathGeometry
  {
    public RoundedRectangleGeometry(Rect bounds, double radius)
    {
      var segments = new PathSegments();
      segments.AddRange(CreateRoundedRectangle(bounds, radius));
      Figures = new PathFigures {
        new PathFigure {
          StartPoint = bounds.TopLeft + new Point(radius,0),
          Segments = segments
        }
      };
    }

    private static IEnumerable<PathSegment> CreateRoundedRectangle(Rect bounds, double radius)
    {
      var cornerSize = new Size(radius, radius);
      var hMove = new Point(radius, 0);
      var vMove = new Point(0, radius);
      yield return new LineSegment { Point = bounds.TopRight - hMove };
      yield return new ArcSegment { Point = bounds.TopRight + vMove, Size = cornerSize, SweepDirection = SweepDirection.Clockwise };
      yield return new LineSegment { Point = bounds.BottomRight - vMove };
      yield return new ArcSegment { Point = bounds.BottomRight - hMove, Size = cornerSize, SweepDirection = SweepDirection.Clockwise };
      yield return new LineSegment { Point = bounds.BottomLeft + hMove };
      yield return new ArcSegment { Point = bounds.BottomLeft - vMove, Size = cornerSize, SweepDirection = SweepDirection.Clockwise };
      yield return new LineSegment { Point = bounds.TopLeft + vMove };
      yield return new ArcSegment { Point = bounds.TopLeft + hMove, Size = cornerSize, SweepDirection = SweepDirection.Clockwise };
    }
  }
}