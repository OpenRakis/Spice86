namespace Spice86.Converters;

using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

using Iced.Intel;

using System.Collections.Generic;

/// <summary>
/// Converts FormatterTextKind to Brushes for syntax highlighting in the disassembly view.
/// </summary>
public static class FormatterTextKindToBrushConverter
{
    // Dark theme brushes
    private static readonly Dictionary<FormatterTextKind, IBrush> DarkBrushes = new()
    {
        { FormatterTextKind.Directive, new SolidColorBrush(Color.FromRgb(128, 170, 255)) },
        { FormatterTextKind.Keyword, new SolidColorBrush(Color.FromRgb(86, 156, 214)) },
        { FormatterTextKind.LabelAddress, new SolidColorBrush(Color.FromRgb(78, 201, 176)) },
        { FormatterTextKind.FunctionAddress, new SolidColorBrush(Color.FromRgb(78, 201, 176)) },
        { FormatterTextKind.Label, new SolidColorBrush(Color.FromRgb(78, 201, 176)) },
        { FormatterTextKind.Function, new SolidColorBrush(Color.FromRgb(78, 201, 176)) },
        { FormatterTextKind.Mnemonic, new SolidColorBrush(Color.FromRgb(86, 156, 214)) },
        { FormatterTextKind.Number, new SolidColorBrush(Color.FromRgb(181, 206, 168)) },
        { FormatterTextKind.Operator, new SolidColorBrush(Color.FromRgb(212, 212, 212)) },
        { FormatterTextKind.Prefix, new SolidColorBrush(Color.FromRgb(86, 156, 214)) },
        { FormatterTextKind.Punctuation, new SolidColorBrush(Color.FromRgb(212, 212, 212)) },
        { FormatterTextKind.Register, new SolidColorBrush(Color.FromRgb(214, 157, 133)) },
        { FormatterTextKind.Text, new SolidColorBrush(Color.FromRgb(212, 212, 212)) }
    };

    // Light theme brushes
    private static readonly Dictionary<FormatterTextKind, IBrush> LightBrushes = new()
    {
        { FormatterTextKind.Directive, new SolidColorBrush(Color.FromRgb(0, 0, 255)) },
        { FormatterTextKind.Keyword, new SolidColorBrush(Color.FromRgb(0, 0, 255)) },
        { FormatterTextKind.LabelAddress, new SolidColorBrush(Color.FromRgb(43, 145, 175)) },
        { FormatterTextKind.FunctionAddress, new SolidColorBrush(Color.FromRgb(43, 145, 175)) },
        { FormatterTextKind.Label, new SolidColorBrush(Color.FromRgb(43, 145, 175)) },
        { FormatterTextKind.Function, new SolidColorBrush(Color.FromRgb(43, 145, 175)) },
        { FormatterTextKind.Mnemonic, new SolidColorBrush(Color.FromRgb(0, 0, 255)) },
        { FormatterTextKind.Number, new SolidColorBrush(Color.FromRgb(128, 0, 128)) },
        { FormatterTextKind.Operator, new SolidColorBrush(Color.FromRgb(0, 0, 0)) },
        { FormatterTextKind.Prefix, new SolidColorBrush(Color.FromRgb(0, 0, 255)) },
        { FormatterTextKind.Punctuation, new SolidColorBrush(Color.FromRgb(0, 0, 0)) },
        { FormatterTextKind.Register, new SolidColorBrush(Color.FromRgb(128, 0, 0)) },
        { FormatterTextKind.Text, new SolidColorBrush(Color.FromRgb(0, 0, 0)) }
    };

    /// <summary>
    /// Gets a brush for the specified formatter text kind.
    /// </summary>
    /// <param name="kind">The formatter text kind.</param>
    /// <returns>A brush for the specified formatter text kind.</returns>
    public static IBrush GetBrush(FormatterTextKind kind)
    {
        // Determine if we're in dark mode
        bool isDarkTheme = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
        
        Dictionary<FormatterTextKind, IBrush> brushes = isDarkTheme ? DarkBrushes : LightBrushes;
        
        return brushes.TryGetValue(kind, out IBrush? brush) 
            ? brush 
            : new SolidColorBrush(isDarkTheme ? Color.FromRgb(212, 212, 212) : Color.FromRgb(0, 0, 0));
    }
}
