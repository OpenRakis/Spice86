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
            existingHandler.Unsubscribe();
            _eventHandlers.Remove(textSticker);
        }

        if (e.NewValue.Value) {
            // Create and store new event handler
            WeakEventHandler handler = new(textSticker, application);
            _eventHandlers.Add(textSticker, handler);
            
            // Subscribe to events
            handler.Subscribe();

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
    /// Event handler wrapper that uses a weak reference to the TextSticker to avoid preventing
    /// its garbage collection, while properly unsubscribing from the Application's theme change
    /// event when the control is unloaded.
    /// </summary>
    private sealed class WeakEventHandler {
        private readonly WeakReference<TextSticker> _weakReference;
        private readonly Application _application;
        private readonly EventHandler _themeChangedHandler;
        private readonly EventHandler<global::Avalonia.Interactivity.RoutedEventArgs> _unloadedHandler;

        public WeakEventHandler(TextSticker textSticker, Application application) {
            _weakReference = new WeakReference<TextSticker>(textSticker);
            _application = application;
            _themeChangedHandler = OnThemeChanged;
            _unloadedHandler = OnUnloaded;
        }

        public void Subscribe() {
            if (_weakReference.TryGetTarget(out TextSticker? textSticker)) {
                _application.ActualThemeVariantChanged += _themeChangedHandler;
                textSticker.Unloaded += _unloadedHandler;
            }
        }

        public void Unsubscribe() {
            _application.ActualThemeVariantChanged -= _themeChangedHandler;
            if (_weakReference.TryGetTarget(out TextSticker? textSticker)) {
                textSticker.Unloaded -= _unloadedHandler;
            }
        }

        private void OnThemeChanged(object? sender, EventArgs e) {
            if (_weakReference.TryGetTarget(out TextSticker? textSticker)) {
                ApplyTheme(textSticker);
            }
        }

        private void OnUnloaded(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) {
            // Clean up when control is unloaded
            Unsubscribe();
            // The sender is the TextSticker that's being unloaded
            if (sender is TextSticker textSticker) {
                _eventHandlers.Remove(textSticker);
            }
        }
    }
}

