namespace Spice86.Converters;

using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

using Spice86.ViewModels;

using System;
using System.Globalization;

internal class BreakpointToBrushConverter : IValueConverter {
    private static readonly SolidColorBrush DarkRed = new SolidColorBrush(Color.FromRgb(128, 0,0));
    private static readonly SolidColorBrush LightRed = new SolidColorBrush(Color.FromRgb(255, 150,150));

    private static readonly SolidColorBrush LightGrey = new SolidColorBrush(Color.FromRgb(240, 240, 240));
    private static readonly SolidColorBrush DarkGrey = new SolidColorBrush(Color.FromRgb(64, 64, 64));
    
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is BreakpointViewModel source) {
            ThemeVariant themeVariant = Application.Current!.ActualThemeVariant;
            if (source.IsEnabled) {
                return themeVariant == ThemeVariant.Dark ? DarkRed : LightRed;
            } 
            else {
                return themeVariant == ThemeVariant.Dark ? DarkGrey : LightGrey;
            }
        }
        else {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return BindingOperations.DoNothing;
    }
}
