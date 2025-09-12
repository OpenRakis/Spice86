using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;

namespace AvaloniaGraphControl
{
  class AglToAvalonia
  {
    private readonly Microsoft.Msagl.Core.Geometry.Point origin;

    public AglToAvalonia(Microsoft.Msagl.Core.Geometry.Point origin)
    {
      this.origin = origin;
    }

    public Point Convert(Microsoft.Msagl.Core.Geometry.Point pt)
    {
      return new Point(pt.X - origin.X, origin.Y - pt.Y);
    }

    public static Size Convert(Microsoft.Msagl.Core.DataStructures.Size size)
    {
      return new Size(size.Width, size.Height);
    }

    public Rect Convert(Microsoft.Msagl.Core.Geometry.Rectangle rect)
    {
      return new Rect(Convert(rect.LeftTop), Convert(rect.RightBottom));
    }

    public PathFigure Convert(Microsoft.Msagl.Core.Geometry.Curves.ICurve curve)
    {
      var segments = new PathSegments();
      segments.AddRange(Flatten(curve).Select(s => TransformSegment(s)));
      return new PathFigure
      {
        StartPoint = Convert(curve.Start),
        Segments = segments,
        IsClosed = false,
        IsFilled = false
      };
    }

    private IEnumerable<Microsoft.Msagl.Core.Geometry.Curves.ICurve> Flatten(Microsoft.Msagl.Core.Geometry.Curves.ICurve curve)
    {
      if (curve is Microsoft.Msagl.Core.Geometry.Curves.Curve compositeCurve)
        return compositeCurve.Segments.SelectMany(c => Flatten(c));
      return Enumerable.Repeat(curve, 1);
    }
    private PathSegment TransformSegment(Microsoft.Msagl.Core.Geometry.Curves.ICurve curve)
    {
      if (curve is Microsoft.Msagl.Core.Geometry.Curves.LineSegment lineSegment)
        return new LineSegment { Point = Convert(lineSegment.End) };
      if (curve is Microsoft.Msagl.Core.Geometry.Curves.CubicBezierSegment bezierSegment)
        return new BezierSegment
        {
          Point1 = Convert(bezierSegment.B(1)),
          Point2 = Convert(bezierSegment.B(2)),
          Point3 = Convert(bezierSegment.End)
        };
      if (curve is Microsoft.Msagl.Core.Geometry.Curves.Ellipse ellipse)
        return ApproximateEllipticalArcWithBezierCurve_ThisMethodNeedsTesting(ellipse);
      throw new NotImplementedException(string.Format("Cannot transform {0} of type {1}", curve, curve.GetType().FullName));
    }

    private PathSegment ApproximateEllipticalArcWithBezierCurve_ThisMethodNeedsTesting(Microsoft.Msagl.Core.Geometry.Curves.Ellipse ellipse)
    {
      // see http://www.spaceroots.org/documents/ellipse/elliptical-arc.pdf
      var d = ellipse.ParEnd - ellipse.ParStart;
      var a = Math.Sin(d) * (Math.Sqrt(4 + 3 * Math.Pow(Math.Tan(d / 2), 2)) - 1) / 3;
      var q1 = ellipse.Start + a * ellipse.Derivative(ellipse.ParStart);
      var q2 = ellipse.End - a * ellipse.Derivative(ellipse.ParEnd);
      return new BezierSegment
      {
        Point1 = Convert(q1),
        Point2 = Convert(q2),
        Point3 = Convert(ellipse.End)
      };
    }

  }
}
