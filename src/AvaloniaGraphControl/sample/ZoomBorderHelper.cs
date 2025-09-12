using Avalonia;
using Avalonia.Controls.PanAndZoom;
using Avalonia.Input;

namespace AvaloniaGraphControlSample;

public class ZoomBorderHelper
{
  private ZoomBorder Border { get; }
  
  void PinchHandler(object? sender, PinchEventArgs e)
  {
    var scaleFactor = 20.0;
    var scale = 1 + (e.Scale - 1) / scaleFactor;
    var point = ((e.ScaleOrigin * Border.TransformToVisual(Border.Child!))!).Value;
    Border.ZoomTo(scale, point.X, point.Y);
  }
  
  public void Reset() => Border.Uniform();
  
  public ZoomBorderHelper(ZoomBorder border)
  {
    Border = border;
    Border.AttachedToVisualTree += (_, _) => Border.AddHandler(Gestures.PinchEvent, PinchHandler);
    Border.DetachedFromVisualTree += (_, _) => Border.RemoveHandler(Gestures.PinchEvent, PinchHandler);
  }

}
