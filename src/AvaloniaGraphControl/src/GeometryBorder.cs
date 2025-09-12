using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AvaloniaGraphControl
{
  public class GeometryBorder : Decorator
  {
    public static readonly StyledProperty<IBrush> BackgroundProperty = AvaloniaProperty.Register<GeometryBorder, IBrush>(nameof(Background));
    public static readonly StyledProperty<IBrush> BorderBrushProperty = AvaloniaProperty.Register<GeometryBorder, IBrush>(nameof(BorderBrush), Brushes.Black);
    public static readonly StyledProperty<double> BorderThicknessProperty = AvaloniaProperty.Register<GeometryBorder, double>(nameof(BorderThickness), 1);
    public static readonly StyledProperty<Geometry> GeometryProperty = AvaloniaProperty.Register<GeometryBorder, Geometry>(nameof(Geometry));

    static GeometryBorder()
    {
      AffectsMeasure<GeometryBorder>(GeometryProperty, BorderThicknessProperty);
      AffectsRender<GeometryBorder>(BackgroundProperty, BorderBrushProperty);
      BackgroundProperty.Changed.AddClassHandler<GeometryBorder>((gb, ea) => gb.Drawing.Brush = (IBrush)ea.NewValue);
      BorderBrushProperty.Changed.AddClassHandler<GeometryBorder>((gb, ea) => gb.Drawing.Pen = new Pen((IBrush)ea.NewValue));
      GeometryProperty.Changed.AddClassHandler<GeometryBorder>((gb, ea) => gb.Drawing.Geometry = (Geometry)ea.NewValue);
    }

    public IBrush Background
    {
      get => GetValue(BackgroundProperty);
      set => SetValue(BackgroundProperty, value);
    }

    public IBrush BorderBrush
    {
      get => GetValue(BorderBrushProperty);
      set => SetValue(BorderBrushProperty, value);
    }

    public double BorderThickness
    {
      get => GetValue(BorderThicknessProperty);
      set => SetValue(BorderThicknessProperty, value);
    }
    public Geometry Geometry
    {
      get => GetValue(GeometryProperty);
      set => SetValue(GeometryProperty, value);
    }

    public GeometryBorder()
    {
      Drawing = new GeometryDrawing
      {
        Brush = Background,
        Pen = new Pen(BorderBrush, BorderThickness)
      };
    }

    public override void Render(DrawingContext context)
    {
      Drawing.Draw(context);
    }

    public readonly GeometryDrawing Drawing;

    protected override Size MeasureOverride(Size availableSize)
    {
      return LayoutHelper.MeasureChild(Child, availableSize, Padding);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
      return LayoutHelper.ArrangeChild(Child, finalSize, Padding);
    }

  }
}