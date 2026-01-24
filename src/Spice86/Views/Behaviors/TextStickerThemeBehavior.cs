namespace Spice86.Views.Behaviors;

using System;
using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Reactive;

using AvaloniaGraphControl;

using Spice86.Views.Converters;

/// <summary>
/// Provides theme-aware styling for TextSticker controls from AvaloniaGraphControl.
/// This allows using the unmodified NuGet package while still supporting dynamic theming.
/// </summary>
public static class TextStickerThemeBehavior {
    private static readonly ConditionalWeakTable<TextSticker, EventHandler> _eventHandlers = new();

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

        if (Application.Current is null) {
            return;
        }

        // Always unsubscribe existing handler first to prevent duplicate subscriptions
        if (_eventHandlers.TryGetValue(textSticker, out EventHandler? existingHandler)) {
            Application.Current.ActualThemeVariantChanged -= existingHandler;
            _eventHandlers.Remove(textSticker);
        }

        if (e.NewValue.Value) {
            // Create and store new event handler
            EventHandler handler = (sender, args) => ApplyTheme(textSticker);
            _eventHandlers.Add(textSticker, handler);
            
            // Subscribe to theme changes
            Application.Current.ActualThemeVariantChanged += handler;

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

