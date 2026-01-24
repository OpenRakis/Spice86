namespace Spice86.Views.Behaviors;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Reactive;

using AvaloniaGraphControl;

using Spice86.Views.Converters;

/// <summary>
/// Provides theme-aware styling for TextSticker controls from AvaloniaGraphControl.
/// This allows using the unmodified NuGet package while still supporting dynamic theming.
/// </summary>
public static class TextStickerThemeBehavior {
    public static readonly AttachedProperty<bool> EnableThemingProperty =
        AvaloniaProperty.RegisterAttached<TextSticker, bool>(
            "EnableTheming",
            typeof(TextStickerThemeBehavior),
            defaultValue: false);

    static TextStickerThemeBehavior() {
        EnableThemingProperty.Changed.Subscribe(new AnonymousObserver<AvaloniaPropertyChangedEventArgs<bool>>(OnEnableThemingChanged));
    }

    public static void SetEnableTheming(Control element, bool value) =>
        element.SetValue(EnableThemingProperty, value);

    public static bool GetEnableTheming(Control element) =>
        element.GetValue(EnableThemingProperty);

    private static void OnEnableThemingChanged(AvaloniaPropertyChangedEventArgs<bool> e) {
        if (e.Sender is not TextSticker textSticker) {
            return;
        }

        if (e.NewValue.Value) {
            // Subscribe to theme changes
            if (Application.Current is not null) {
                Application.Current.ActualThemeVariantChanged += (sender, args) => ApplyTheme(textSticker);
            }

            // Apply initial theme
            ApplyTheme(textSticker);
        }
    }

    private static void ApplyTheme(TextSticker textSticker) {
        // Apply theme-aware colors using the same resources as the XAML styles
        textSticker.TextForeground = HighlightingConverter.GetDefaultForegroundBrush();
        textSticker.Background = HighlightingConverter.GetDefaultBackgroundBrush();
    }
}

