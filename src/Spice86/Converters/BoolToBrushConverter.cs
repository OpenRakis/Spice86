namespace Spice86.Converters;

using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

using System;
using System.Globalization;

internal class BoolToBrushConverter : IValueConverter {
    private static readonly SolidColorBrush DarkRed = new SolidColorBrush(Color.FromRgb(128, 0,0));
    private static readonly SolidColorBrush LightRed = new SolidColorBrush(Color.FromRgb(255, 150,150));
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is bool source) {
            if(source is true) {
                ThemeVariant themeVariant = Application.Current!.ActualThemeVariant;
                return themeVariant == ThemeVariant.Dark ? DarkRed : LightRed;
            }
            return null;
        }
        return BindingOperations.DoNothing;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return BindingOperations.DoNothing;
    }
}
