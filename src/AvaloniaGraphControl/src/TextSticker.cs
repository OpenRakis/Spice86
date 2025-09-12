using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AvaloniaGraphControl
{
  public class TextSticker : Control
  {
    public static readonly StyledProperty<IBrush> BackgroundProperty = AvaloniaProperty.Register<TextSticker, IBrush>(nameof(Background), Brushes.Transparent);
    public static readonly StyledProperty<IBrush> BorderForegroundProperty = AvaloniaProperty.Register<TextSticker, IBrush>(nameof(BorderForeground), Brushes.Black);
    public static readonly StyledProperty<IBrush> TextForegroundProperty = AvaloniaProperty.Register<TextSticker, IBrush>(nameof(TextForeground), Brushes.Black);
    public static readonly StyledProperty<double> BorderThicknessProperty = AvaloniaProperty.Register<TextSticker, double>(nameof(BorderThickness), 1);
    public static readonly StyledProperty<double> BorderRadiusProperty = AvaloniaProperty.Register<TextSticker, double>(nameof(BorderRadius), 3);
    public static readonly StyledProperty<FontWeight> FontWeightProperty = AvaloniaProperty.Register<TextSticker, FontWeight>(nameof(FontWeight), FontWeight.Normal);
    public static readonly StyledProperty<FontStyle> FontStyleProperty = AvaloniaProperty.Register<TextSticker, FontStyle>(nameof(FontStyle));
    public static readonly StyledProperty<double> FontSizeProperty = AvaloniaProperty.Register<TextSticker, double>(nameof(FontSize), 12);
    public static readonly StyledProperty<FontFamily> FontFamilyProperty = AvaloniaProperty.Register<TextSticker, FontFamily>(nameof(FontFamily), FontFamily.Default);
    public static readonly StyledProperty<Thickness> PaddingProperty = AvaloniaProperty.Register<TextSticker, Thickness>(nameof(Padding), new Thickness(10, 5));

    public enum Shapes
    {
      RoundedRectangle = 0,
      Rectangle,
      Ellipse,
      Diamond
    }
    public static readonly StyledProperty<Shapes> ShapeProperty = AvaloniaProperty.Register<TextSticker, Shapes>(nameof(Shape));
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<TextSticker, string>(nameof(Text));

    static TextSticker()
    {
      AffectsMeasure<TextSticker>(ShapeProperty, BorderThicknessProperty, FontWeightProperty, FontStyleProperty, FontSizeProperty, FontFamilyProperty, PaddingProperty, TextProperty);
      AffectsRender<TextSticker>(BackgroundProperty, BorderForegroundProperty, TextForegroundProperty, HorizontalAlignmentProperty, VerticalAlignmentProperty);
      BackgroundProperty.Changed.AddClassHandler<TextSticker>((ts, ea) => ts.border.Background = (IBrush)ea.NewValue);
      BorderForegroundProperty.Changed.AddClassHandler<TextSticker>((ts, ea) => ts.border.BorderBrush = (IBrush)ea.NewValue);
      TextForegroundProperty.Changed.AddClassHandler<TextSticker>((ts, ea) => ts.textBlock.Foreground = (IBrush)ea.NewValue);
      BorderThicknessProperty.Changed.AddClassHandler<TextSticker>((ts, ea) => ts.border.BorderThickness = (double)ea.NewValue);
      FontWeightProperty.Changed.AddClassHandler<TextSticker>((ts, ea) => ts.textBlock.FontWeight = (FontWeight)ea.NewValue);
      FontStyleProperty.Changed.AddClassHandler<TextSticker>((ts, ea) => ts.textBlock.FontStyle = (FontStyle)ea.NewValue);
      FontSizeProperty.Changed.AddClassHandler<TextSticker>((ts, ea) => ts.textBlock.FontSize = (double)ea.NewValue);
      FontFamilyProperty.Changed.AddClassHandler<TextSticker>((ts, ea) => ts.textBlock.FontFamily = (FontFamily)ea.NewValue);
      TextProperty.Changed.AddClassHandler<TextSticker>((ts, ea) => ts.textBlock.Text = (string)ea.NewValue);
      HorizontalAlignmentProperty.Changed.AddClassHandler<TextSticker>((ts, ea) => ts.textBlock.HorizontalAlignment = (HorizontalAlignment)ea.NewValue);
      VerticalAlignmentProperty.Changed.AddClassHandler<TextSticker>((ts, ea) => ts.textBlock.VerticalAlignment = (VerticalAlignment)ea.NewValue);
      PaddingProperty.Changed.AddClassHandler<TextSticker>((ts, ea) => ts.border.Padding = (Thickness)ea.NewValue);
    }

    public IBrush Background
    {
      get => GetValue(BackgroundProperty);
      set => SetValue(BackgroundProperty, value);
    }
    public IBrush BorderForeground
    {
      get => GetValue(BorderForegroundProperty);
      set => SetValue(BorderForegroundProperty, value);
    }
    public IBrush TextForeground
    {
      get => GetValue(TextForegroundProperty);
      set => SetValue(TextForegroundProperty, value);
    }
    public double BorderThickness
    {
      get => GetValue(BorderThicknessProperty);
      set => SetValue(BorderThicknessProperty, value);
    }
    public double BorderRadius
    {
      get => GetValue(BorderRadiusProperty);
      set => SetValue(BorderRadiusProperty, value);
    }
    public Shapes Shape
    {
      get => GetValue(ShapeProperty);
      set => SetValue(ShapeProperty, value);
    }
    public FontWeight FontWeight
    {
      get => GetValue(FontWeightProperty);
      set => SetValue(FontWeightProperty, value);
    }
    public FontStyle FontStyle
    {
      get => GetValue(FontStyleProperty);
      set => SetValue(FontStyleProperty, value);
    }
    public double FontSize
    {
      get => GetValue(FontSizeProperty);
      set => SetValue(FontSizeProperty, value);
    }
    public FontFamily FontFamily
    {
      get => GetValue(FontFamilyProperty);
      set => SetValue(FontFamilyProperty, value);
    }
    public string Text
    {
      get => GetValue(TextProperty);
      set => SetValue(TextProperty, value);
    }
    public Thickness Padding
    {
      get => GetValue(PaddingProperty);
      set => SetValue(PaddingProperty, value);
    }

    private readonly GeometryBorder border;
    private readonly TextBlock textBlock;

    public TextSticker()
    {
      textBlock = new TextBlock
      {
        Foreground = TextForeground,
        FontWeight = FontWeight,
        FontStyle = FontStyle,
        FontSize = FontSize,
        FontFamily = FontFamily
      };

      border = new GeometryBorder
      {
        Background = Background,
        BorderBrush = BorderForeground,
        BorderThickness = BorderThickness,
        Padding = Padding,
        Child = textBlock
      };

      VisualChildren.Add(border);

      HorizontalAlignment = HorizontalAlignment.Center;
      VerticalAlignment = VerticalAlignment.Center;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
      border.Measure(availableSize);
      var bounds = new Rect(border.DesiredSize);
      return bounds.Size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
      border.Geometry = ComputeGeometry(new Rect(finalSize));
      return base.ArrangeOverride(finalSize);
    }

    private Geometry ComputeGeometry(Rect bounds) => Shape switch
    {
      Shapes.Rectangle => new RectangleGeometry(bounds),
      Shapes.Ellipse => new EllipseGeometry(bounds),
      Shapes.Diamond => new DiamondGeometry(bounds),
      _ => new RoundedRectangleGeometry(bounds, BorderRadius)
    };
  }
}