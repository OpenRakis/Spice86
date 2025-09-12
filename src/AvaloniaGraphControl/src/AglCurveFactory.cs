
using Avalonia;
using Microsoft.Msagl.Core.Geometry.Curves;
using static AvaloniaGraphControl.TextSticker;

namespace AvaloniaGraphControl
{
  class AglCurveFactory
  {

    public static ICurve Create(Shapes shape, Size size, double cornerRadius) => shape switch
    {
      Shapes.Rectangle => CurveFactory.CreateRectangle(size.Width, size.Height, Origin),
      Shapes.Ellipse => CurveFactory.CreateEllipse(size.Width / 2, size.Height / 2, Origin),
      Shapes.Diamond => CurveFactory.CreateDiamond(size.Width / 2, size.Height / 2, Origin),
      _ => CurveFactory.CreateRectangleWithRoundedCorners(size.Width, size.Height, cornerRadius, cornerRadius, Origin)
    };

    private static Microsoft.Msagl.Core.Geometry.Point Origin = new Microsoft.Msagl.Core.Geometry.Point(0, 0);
  }
}