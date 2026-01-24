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
    private static readonly ConditionalWeakTable<TextSticker, WeakEventHandler> _eventHandlers = new();

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

        Application? application = Application.Current;
        if (application is null) {
            return;
        }

        // Always unsubscribe existing handler first to prevent duplicate subscriptions
        if (_eventHandlers.TryGetValue(textSticker, out WeakEventHandler? existingHandler)) {
            application.ActualThemeVariantChanged -= existingHandler.Handler;
            _eventHandlers.Remove(textSticker);
        }

        if (e.NewValue.Value) {
            // Create and store new event handler with weak reference
            WeakEventHandler handler = new(textSticker, application);
            _eventHandlers.Add(textSticker, handler);
            
            // Subscribe to theme changes
            application.ActualThemeVariantChanged += handler.Handler;

            // Apply initial theme
            ApplyTheme(textSticker);
        }
    }

    private static void ApplyTheme(TextSticker textSticker) {
        // Apply theme-aware colors using the same resources as the XAML styles
        textSticker.TextForeground = HighlightingConverter.GetDefaultForegroundBrush();
        textSticker.Background = HighlightingConverter.GetDefaultBackgroundBrush();
    }

    /// <summary>
    /// Weak event handler wrapper that doesn't prevent garbage collection of the target control.
    /// Automatically unsubscribes when the target is no longer available.
    /// </summary>
    private sealed class WeakEventHandler {
        private readonly WeakReference<TextSticker> _weakReference;
        private readonly WeakReference<Application> _applicationReference;
        private readonly EventHandler _cachedHandler;

        public WeakEventHandler(TextSticker textSticker, Application application) {
            _weakReference = new WeakReference<TextSticker>(textSticker);
            _applicationReference = new WeakReference<Application>(application);
            _cachedHandler = OnThemeChanged;
        }

        public EventHandler Handler => _cachedHandler;

        private void OnThemeChanged(object? sender, EventArgs e) {
            if (_weakReference.TryGetTarget(out TextSticker? textSticker)) {
                ApplyTheme(textSticker);
            } else if (_applicationReference.TryGetTarget(out Application? application)) {
                // Target is gone, unsubscribe to prevent further invocations
                application.ActualThemeVariantChanged -= _cachedHandler;
            }
        }
    }
}

