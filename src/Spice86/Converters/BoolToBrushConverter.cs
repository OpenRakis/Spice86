using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

using Spice86.Models.Debugging;

using System.Globalization;

namespace Spice86.Converters;

public class BoolToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush DarkBlue = new SolidColorBrush(Color.FromRgb(0, 0, 128));
    private static readonly SolidColorBrush LightBlue = new SolidColorBrush(Color.FromRgb(150, 150, 255));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is CpuInstructionInfo source)
        {
            ThemeVariant themeVariant = Application.Current!.ActualThemeVariant;
            if (source.IsCsIp)
            {
                return themeVariant == ThemeVariant.Dark ? DarkBlue : LightBlue;
            }
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return BindingOperations.DoNothing;
    }
}
