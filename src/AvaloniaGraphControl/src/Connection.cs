
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AvaloniaGraphControl
{
  public class Connection : Control
  {
    public static readonly StyledProperty<IBrush> BrushProperty = AvaloniaProperty.Register<Connection, IBrush>(nameof(Brush), Brushes.Black);
    public IBrush Brush
    {
      get => GetValue(BrushProperty);
      set => SetValue(BrushProperty, value);
    }

    private readonly List<Drawing> Drawings;

    public Connection()
    {
      this.Drawings = new List<Drawing>();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
      Drawings.Clear();
      var edge = (Edge)this.DataContext;
      var dEdge = edge.DEdge;
      var box = dEdge.BoundingBox;
      var a2a = new AglToAvalonia(box.LeftTop);
      var nofill = new SolidColorBrush();
      Drawings.Add(FigureToDrawing(CreateEdgePathFigure(dEdge, a2a), Brush, nofill));
      if (edge.HeadSymbol == Edge.Symbol.Arrow)
        Drawings.Add(FigureToDrawing(CreateArrowHeadFigure(dEdge.EdgeCurve.End, dEdge.ArrowAtTargetPosition, a2a), Brush, Brush));
      if (edge.TailSymbol == Edge.Symbol.Arrow)
        Drawings.Add(FigureToDrawing(CreateArrowHeadFigure(dEdge.EdgeCurve.Start, dEdge.ArrowAtSourcePosition, a2a), Brush, Brush));
      return AglToAvalonia.Convert(box.Size);
    }
    public override void Render(DrawingContext context)
    {
      foreach (var drawing in Drawings)
      {
        drawing.Draw(context);
      }
    }
    private static Drawing FigureToDrawing(PathFigure figure, IBrush strokeBrush, IBrush fillBrush)
    {
      return new GeometryDrawing()
      {
        Pen = new Pen(strokeBrush),
        Brush = fillBrush,
        Geometry = new PathGeometry
        {
          Figures = new PathFigures
          {
            figure
          }
        }
      };
    }

    private static PathFigure CreateEdgePathFigure(Microsoft.Msagl.Drawing.Edge edge, AglToAvalonia a2a)
    {
      return a2a.Convert(edge.EdgeCurve);
    }

    private static PathFigure CreateArrowHeadFigure(Microsoft.Msagl.Core.Geometry.Point origin, Microsoft.Msagl.Core.Geometry.Point target, AglToAvalonia a2a)
    {
      var arrowHead = ComputeArrowHead(origin, target, 3.0).ToList();
      var segments = new PathSegments();
      segments.AddRange(arrowHead.Skip(1).Select(p => new LineSegment { Point = a2a.Convert(p) }));
      var figure = new PathFigure { IsFilled = true, IsClosed = true, StartPoint = a2a.Convert(arrowHead.First()), Segments = segments };
      return figure;
    }

    private static IEnumerable<Microsoft.Msagl.Core.Geometry.Point> ComputeArrowHead(
      Microsoft.Msagl.Core.Geometry.Point origin,
      Microsoft.Msagl.Core.Geometry.Point target,
      double width
    )
    {
      yield return target;
      var v = target - origin;
      if (v.Y == 0)
      {
        yield return origin + new Microsoft.Msagl.Core.Geometry.Point(0, width);
        yield return origin + new Microsoft.Msagl.Core.Geometry.Point(0, -width);
      }
      else
      {
        var a = -v.X / v.Y;
        var x = Math.Sqrt(width * width / (1 + a * a));
        yield return origin + new Microsoft.Msagl.Core.Geometry.Point(x, a * x);
        yield return origin + new Microsoft.Msagl.Core.Geometry.Point(-x, -a * x);
      }
    }
  }
}