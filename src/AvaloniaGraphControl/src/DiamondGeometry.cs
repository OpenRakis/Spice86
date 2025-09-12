using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;

namespace AvaloniaGraphControl
{
  class DiamondGeometry : PolylineGeometry
  {
    public DiamondGeometry(Rect bounds) : base(CreateDiamond(bounds), false)
    {
    }

    private static IEnumerable<Point> CreateDiamond(Rect bounds)
    {
      yield return (bounds.TopLeft + bounds.TopRight) / 2;
      yield return (bounds.TopRight + bounds.BottomRight) / 2;
      yield return (bounds.BottomRight + bounds.BottomLeft) / 2;
      yield return (bounds.BottomLeft + bounds.TopLeft) / 2;
      yield return (bounds.TopLeft + bounds.TopRight) / 2;
    }
  }
}