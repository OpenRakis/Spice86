namespace Spice86.Views.Behaviors;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Reactive;
using Avalonia.Styling;
using Avalonia.Media;

using Spice86.Views.Converters;

/// <summary>
/// Provides highlight behaviors for different control types with fade-out animations.
/// </summary>
public static class HighlightBehavior {
    // Shared animation configuration constants
    private static readonly TimeSpan AnimationDuration = TimeSpan.FromSeconds(0.5);
    private const FillMode AnimationFillMode = FillMode.Forward;

    public static readonly AttachedProperty<bool> IsHighlightedProperty = AvaloniaProperty.RegisterAttached<TextBlock, bool>("IsHighlighted", typeof(HighlightBehavior), defaultValue: false);

    public static readonly AttachedProperty<bool> HighlightForegroundProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, bool>("HighlightForeground", typeof(HighlightBehavior), defaultValue: true);

    static HighlightBehavior() {
        IsHighlightedProperty.Changed.Subscribe(new AnonymousObserver<AvaloniaPropertyChangedEventArgs<bool>>(OnIsHighlightedChanged));
        IsContentHighlightedProperty.Changed.Subscribe(new AnonymousObserver<AvaloniaPropertyChangedEventArgs<bool>>(OnIsContentHighlightedChanged));
        IsPanelHighlightedProperty.Changed.Subscribe(new AnonymousObserver<AvaloniaPropertyChangedEventArgs<bool>>(OnIsPanelHighlightedChanged));
    }

    public static void SetIsHighlighted(TextBlock element, bool value) =>
        element.SetValue(IsHighlightedProperty, value);

    public static bool GetIsHighlighted(TextBlock element) =>
        element.GetValue(IsHighlightedProperty);

    public static void SetHighlightForeground(TextBlock element, bool value) =>
        element.SetValue(HighlightForegroundProperty, value);

    public static bool GetHighlightForeground(TextBlock element) =>
        element.GetValue(HighlightForegroundProperty);

    private const string HighlightBackgroundKey = "DisassemblyLineHighlightBrush";
    private const string HighlightForegroundKey = "DisassemblyLineHighlightForegroundBrush";
    private const string DefaultBackgroundKey = "WindowDefaultBackground";
    private const string DefaultForegroundKey = "DisassemblyTextBrush";

    private static readonly IBrush FallbackHighlightBackgroundDark = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x50));
    private static readonly IBrush FallbackHighlightForegroundDark = new SolidColorBrush(Color.FromRgb(0x1E, 0x90, 0xFF));
    private static readonly IBrush FallbackHighlightBackgroundLight = new SolidColorBrush(Color.FromRgb(0xAD, 0xD6, 0xFF));
    private static readonly IBrush FallbackHighlightForegroundLight = Brushes.Black;

    private static readonly IBrush FallbackDefaultBackground = Brushes.Transparent;
    // Use Gray as fallback for text to be visible on both white and black backgrounds if resource lookup fails
    private static readonly IBrush FallbackDefaultForeground = Brushes.Gray;

    private static async void OnIsHighlightedChanged(AvaloniaPropertyChangedEventArgs<bool> e) {
        if (e.Sender is TextBlock textBlock) {
            bool highlightForeground = GetHighlightForeground(textBlock);

            IBrush fallbackHighlightBg = GetThemeFallback(textBlock, true);
            IBrush fallbackHighlightFg = GetThemeFallback(textBlock, false);

            IBrush highlightBackgroundBrush = GetBrush(textBlock, HighlightBackgroundKey, fallbackHighlightBg);
            IBrush highlightForegroundBrush = GetBrush(textBlock, HighlightForegroundKey, fallbackHighlightFg);
            
            IBrush defaultBackgroundBrush = GetBrush(textBlock, DefaultBackgroundKey, FallbackDefaultBackground);
            IBrush defaultForegroundBrush = GetBrush(textBlock, DefaultForegroundKey, FallbackDefaultForeground);


            if (e.NewValue.Value) {
                // Immediately apply highlight
                textBlock.Background = highlightBackgroundBrush;

                if (highlightForeground) {
                    textBlock.Foreground = highlightForegroundBrush;
                }
            } else if (e.OldValue.Value) {
                // Create animation for background fade-out
                Animation backgroundAnimation = CreateAnimation(TextBlock.BackgroundProperty, highlightBackgroundBrush, defaultBackgroundBrush);

                // Start background animation
                await backgroundAnimation.RunAsync(textBlock);

                // If foreground highlighting is enabled, animate it too
                if (highlightForeground) {
                    Animation foregroundAnimation = CreateAnimation(TextBlock.ForegroundProperty, highlightForegroundBrush,
                        defaultForegroundBrush);

                    // Start foreground animation
                    await foregroundAnimation.RunAsync(textBlock);
                }
            }
        }
    }

    public static readonly AttachedProperty<bool> IsContentHighlightedProperty =
        AvaloniaProperty.RegisterAttached<ContentControl, bool>("IsContentHighlighted", typeof(HighlightBehavior), defaultValue: false);

    public static readonly AttachedProperty<bool> HighlightContentForegroundProperty =
        AvaloniaProperty.RegisterAttached<ContentControl, bool>("HighlightContentForeground", typeof(HighlightBehavior), defaultValue: true);

    public static void SetIsContentHighlighted(ContentControl element, bool value) =>
        element.SetValue(IsContentHighlightedProperty, value);

    public static bool GetIsContentHighlighted(ContentControl element) =>
        element.GetValue(IsContentHighlightedProperty);

    public static void SetHighlightContentForeground(ContentControl element, bool value) =>
        element.SetValue(HighlightContentForegroundProperty, value);

    public static bool GetHighlightContentForeground(ContentControl element) =>
        element.GetValue(HighlightContentForegroundProperty);

    private static async void OnIsContentHighlightedChanged(AvaloniaPropertyChangedEventArgs<bool> e) {
        if (e.Sender is ContentControl contentControl) {
            bool highlightForeground = GetHighlightContentForeground(contentControl);

            IBrush fallbackHighlightBg = GetThemeFallback(contentControl, true);
            IBrush fallbackHighlightFg = GetThemeFallback(contentControl, false);

            IBrush highlightBackgroundBrush = GetBrush(contentControl, HighlightBackgroundKey, fallbackHighlightBg);
            IBrush highlightForegroundBrush = GetBrush(contentControl, HighlightForegroundKey, fallbackHighlightFg);
            
            IBrush defaultBackgroundBrush = GetBrush(contentControl, DefaultBackgroundKey, FallbackDefaultBackground);
            IBrush defaultForegroundBrush = GetBrush(contentControl, DefaultForegroundKey, FallbackDefaultForeground);

            if (e.NewValue.Value) {
                // Immediately apply highlight
                contentControl.Background = highlightBackgroundBrush;

                if (highlightForeground) {
                    contentControl.Foreground = highlightForegroundBrush;
                }
            } else if (e.OldValue.Value) {
                // Create animation for background fade-out
                Animation backgroundAnimation = CreateAnimation(TemplatedControl.BackgroundProperty, highlightBackgroundBrush,
                    defaultBackgroundBrush);

                // Start background animation
                await backgroundAnimation.RunAsync(contentControl);

                // If foreground highlighting is enabled, animate it too
                if (highlightForeground) {
                    Animation foregroundAnimation = CreateAnimation(TemplatedControl.ForegroundProperty, highlightForegroundBrush,
                        defaultForegroundBrush);

                    // Start foreground animation
                    await foregroundAnimation.RunAsync(contentControl);
                }
            }
        }
    }

    public static readonly AttachedProperty<bool> IsPanelHighlightedProperty = AvaloniaProperty.RegisterAttached<Panel, bool>("IsPanelHighlighted", typeof(HighlightBehavior), defaultValue: false);

    public static void SetIsPanelHighlighted(Panel element, bool value) =>
        element.SetValue(IsPanelHighlightedProperty, value);

    public static bool GetIsPanelHighlighted(Panel element) =>
        element.GetValue(IsPanelHighlightedProperty);

    private static async void OnIsPanelHighlightedChanged(AvaloniaPropertyChangedEventArgs<bool> e) {
        if (e.Sender is Panel panel) {
            IBrush fallbackHighlightBg = GetThemeFallback(panel, true);
            IBrush highlightBackgroundBrush = GetBrush(panel, HighlightBackgroundKey, fallbackHighlightBg);
            IBrush defaultBackgroundBrush = GetBrush(panel, DefaultBackgroundKey, FallbackDefaultBackground);

            if (e.NewValue.Value) {
                // Immediately apply highlight
                panel.Background = highlightBackgroundBrush;
            } else if (e.OldValue.Value) {
                // Create animation for background fade-out
                Animation backgroundAnimation = CreateAnimation(Panel.BackgroundProperty, highlightBackgroundBrush, defaultBackgroundBrush);

                // Start background animation
                await backgroundAnimation.RunAsync(panel);
            }
        }
    }

    private static IBrush GetThemeFallback(StyledElement element, bool isBackground) {
        // Default to Dark fallback unless explicitly Light. 
        // This prevents bright 'Light' fallbacks from appearing in Dark mode if ActualThemeVariant is Default.
        bool isLight = element.ActualThemeVariant == ThemeVariant.Light;
        if (isLight) {
            return isBackground ? FallbackHighlightBackgroundLight : FallbackHighlightForegroundLight;
        }
        return isBackground ? FallbackHighlightBackgroundDark : FallbackHighlightForegroundDark;
    }

    private static IBrush GetBrush(StyledElement element, string key, IBrush fallback) {
        if (element.TryGetResource(key, out object? resource) && resource is IBrush brush) {
            return brush;
        }
        return fallback;
    }

    /// <summary>
    /// Creates an animation with the shared duration and fill mode.
    /// </summary>
    /// <param name="property">The property to animate</param>
    /// <param name="startValue">The starting value</param>
    /// <param name="endValue">The ending value</param>
    /// <returns>A configured animation</returns>
    private static Animation CreateAnimation(AvaloniaProperty property, object startValue, object endValue) {
        return new Animation {
            Duration = AnimationDuration,
            FillMode = AnimationFillMode,
            Children = {
                new KeyFrame {
                    Cue = new Cue(0d),
                    Setters = {new Setter(property, startValue)}
                },
                new KeyFrame {
                    Cue = new Cue(1d),
                    Setters = {new Setter(property, endValue)}
                }
            }
        };
    }
}