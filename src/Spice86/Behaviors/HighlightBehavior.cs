namespace Spice86.Behaviors;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;

using Spice86.Converters;

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
        IsHighlightedProperty.Changed.Subscribe(OnIsHighlightedChanged);
        IsContentHighlightedProperty.Changed.Subscribe(OnIsContentHighlightedChanged);
        IsPanelHighlightedProperty.Changed.Subscribe(OnIsPanelHighlightedChanged);
    }

    public static void SetIsHighlighted(TextBlock element, bool value) =>
        element.SetValue(IsHighlightedProperty, value);

    public static bool GetIsHighlighted(TextBlock element) =>
        element.GetValue(IsHighlightedProperty);

    public static void SetHighlightForeground(TextBlock element, bool value) =>
        element.SetValue(HighlightForegroundProperty, value);

    public static bool GetHighlightForeground(TextBlock element) =>
        element.GetValue(HighlightForegroundProperty);

    private static void OnIsHighlightedChanged(AvaloniaPropertyChangedEventArgs<bool> e) {
        if (e.Sender is TextBlock textBlock) {
            bool highlightForeground = GetHighlightForeground(textBlock);

            if (e.NewValue.Value) {
                // Immediately apply highlight
                textBlock.Background = HighlightingConverter.GetHighlightBackgroundBrush();

                if (highlightForeground) {
                    textBlock.Foreground = HighlightingConverter.GetHighlightForegroundBrush();
                }
            } else if (e.OldValue.Value) {
                // Create animation for background fade-out
                Animation backgroundAnimation = CreateAnimation(TextBlock.BackgroundProperty, HighlightingConverter.GetHighlightBackgroundBrush(), HighlightingConverter.GetDefaultBackgroundBrush());

                // Start background animation
                backgroundAnimation.RunAsync(textBlock);

                // If foreground highlighting is enabled, animate it too
                if (highlightForeground) {
                    Animation foregroundAnimation = CreateAnimation(TextBlock.ForegroundProperty, HighlightingConverter.GetHighlightForegroundBrush(),
                        HighlightingConverter.GetDefaultForegroundBrush());

                    // Start foreground animation
                    foregroundAnimation.RunAsync(textBlock);
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

    private static void OnIsContentHighlightedChanged(AvaloniaPropertyChangedEventArgs<bool> e) {
        if (e.Sender is ContentControl contentControl) {
            bool highlightForeground = GetHighlightContentForeground(contentControl);

            if (e.NewValue.Value) {
                // Immediately apply highlight
                contentControl.Background = HighlightingConverter.GetHighlightBackgroundBrush();

                if (highlightForeground) {
                    contentControl.Foreground = HighlightingConverter.GetHighlightForegroundBrush();
                }
            } else if (e.OldValue.Value) {
                // Create animation for background fade-out
                Animation backgroundAnimation = CreateAnimation(TemplatedControl.BackgroundProperty, HighlightingConverter.GetHighlightBackgroundBrush(),
                    HighlightingConverter.GetDefaultBackgroundBrush());

                // Start background animation
                backgroundAnimation.RunAsync(contentControl);

                // If foreground highlighting is enabled, animate it too
                if (highlightForeground) {
                    Animation foregroundAnimation = CreateAnimation(TemplatedControl.ForegroundProperty, HighlightingConverter.GetHighlightForegroundBrush(),
                        HighlightingConverter.GetDefaultForegroundBrush());

                    // Start foreground animation
                    foregroundAnimation.RunAsync(contentControl);
                }
            }
        }
    }

    public static readonly AttachedProperty<bool> IsPanelHighlightedProperty = AvaloniaProperty.RegisterAttached<Panel, bool>("IsPanelHighlighted", typeof(HighlightBehavior), defaultValue: false);

    public static void SetIsPanelHighlighted(Panel element, bool value) =>
        element.SetValue(IsPanelHighlightedProperty, value);

    public static bool GetIsPanelHighlighted(Panel element) =>
        element.GetValue(IsPanelHighlightedProperty);

    private static void OnIsPanelHighlightedChanged(AvaloniaPropertyChangedEventArgs<bool> e) {
        if (e.Sender is Panel panel) {
            if (e.NewValue.Value) {
                // Immediately apply highlight
                panel.Background = HighlightingConverter.GetHighlightBackgroundBrush();
            } else if (e.OldValue.Value) {
                // Create animation for background fade-out
                Animation backgroundAnimation = CreateAnimation(Panel.BackgroundProperty, HighlightingConverter.GetHighlightBackgroundBrush(), HighlightingConverter.GetDefaultBackgroundBrush());

                // Start background animation
                backgroundAnimation.RunAsync(panel);
            }
        }
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