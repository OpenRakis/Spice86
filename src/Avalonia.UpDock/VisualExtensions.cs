using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Avalonia.UpDock;

internal static class VisualExtensions
{
    public static Rect GetBoundsOf(this Visual self, Visual visual) 
    {
        var topLeft = visual.TranslatePoint(new Point(0, 0), self)!.Value;
        var bottomRight = visual.TranslatePoint(new Point(visual.Bounds.Width, visual.Bounds.Height), self)!.Value;
        return new Rect(topLeft, bottomRight);
    }
}
