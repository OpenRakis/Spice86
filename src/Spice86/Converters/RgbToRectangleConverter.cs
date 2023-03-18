namespace Spice86.Converters;

using Avalonia.Controls.Shapes;
using Avalonia.Data.Converters;
using Avalonia.Media;

using Spice86.Shared;

using System.Globalization;

public class RgbToRectangleConverter : IValueConverter {
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is IEnumerable<byte> rawPalette) {
            foreach (var rawItem in rawPalette) {
                var rgb = Color.FromUInt32(rawItem);
                var rectangles = new List<Rectangle>();
                var item = new Rectangle() {
                    Fill = new SolidColorBrush() {
                        Color = Color.FromUInt32(rawItem)
                    }
                };
                rectangles.Add(item);
                return rectangles;
            }
        } else if (value is IEnumerable<Rgb> palette) {
            var rectangles = new List<Rectangle>();
            foreach (var rgb in palette) {
                SolidColorBrush brush = new();
                brush.Color = Color.FromRgb(rgb.R, rgb.G, rgb.B);
                rectangles.Add((new Rectangle(){ Fill = brush}));
            }

            return rectangles;
        }
        return null;

    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is IEnumerable<Rectangle> rectangles) {
            var items = new List<Rgb>();
            foreach (var rect in rectangles) {
                var brush = rect.Fill as SolidColorBrush;
                if (brush is null) {
                    continue;
                }

                var item = new Rgb() {
                    R = brush.Color.R,
                    B = brush.Color.B,
                    G = brush.Color.G
                };
            }

            return items;
        }

        return null;
    }
}