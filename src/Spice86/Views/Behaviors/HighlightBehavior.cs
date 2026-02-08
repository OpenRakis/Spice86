namespace Spice86.Views.Behaviors;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Reactive;
using Avalonia.Styling;
using Avalonia.Threading;

using Spice86.Views.Converters;

using System;
using System.Threading.Tasks;

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

    // Internal flag to prevent starting the same animation multiple times concurrently
    private static readonly AttachedProperty<bool> IsAnimatingProperty = AvaloniaProperty.RegisterAttached<AvaloniaObject, bool>("IsAnimating", typeof(HighlightBehavior), defaultValue: false);

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

    private static void SetIsAnimating(AvaloniaObject element, bool value) =>
        element.SetValue(IsAnimatingProperty, value);

    private static bool GetIsAnimating(AvaloniaObject element) =>
        element.GetValue(IsAnimatingProperty);

    private static void OnIsHighlightedChanged(AvaloniaPropertyChangedEventArgs<bool> e) {
        if (e.Sender is TextBlock textBlock) {
            bool highlightForeground = GetHighlightForeground(textBlock);

            if (e.NewValue.Value) {
                // Immediately apply highlight
                textBlock.Background = HighlightingConverter.GetHighlightBackgroundBrush();

                if (highlightForeground) {
                    textBlock.Foreground = HighlightingConverter.GetHighlightForegroundBrush();
                }

                // If we are explicitly setting highlight, ensure animating flag is cleared
                SetIsAnimating(textBlock, false);
            } else if (e.OldValue.Value) {
                // If already animating, do not start another animation
                if (GetIsAnimating(textBlock)) {
                    return;
                }

                // Mark as animating
                SetIsAnimating(textBlock, true);

                // Create animation for background fade-out
                Animation backgroundAnimation = CreateAnimation(TextBlock.BackgroundProperty, HighlightingConverter.GetHighlightBackgroundBrush(), HighlightingConverter.GetDefaultBackgroundBrush());

                // Start background animation
                Task backgroundTask = backgroundAnimation.RunAsync(textBlock);

                // If foreground highlighting is enabled, animate it too
                if (highlightForeground) {
                    Animation foregroundAnimation = CreateAnimation(TextBlock.ForegroundProperty, HighlightingConverter.GetHighlightForegroundBrush(),
                        HighlightingConverter.GetDefaultForegroundBrush());

                    // Start foreground animation
                    Task foregroundTask = foregroundAnimation.RunAsync(textBlock);

                    // When both complete, clear animating flag on UI thread
                    Task.WhenAll(new Task[] { backgroundTask, foregroundTask }).ContinueWith(_ => {
                        Dispatcher.UIThread.Post(() => SetIsAnimating(textBlock, false));
                    });
                } else {
                    // Only background animation running
                    backgroundTask.ContinueWith(_ => {
                        Dispatcher.UIThread.Post(() => SetIsAnimating(textBlock, false));
                    });
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

                // Clear animating flag when set directly
                SetIsAnimating(contentControl, false);
            } else if (e.OldValue.Value) {
                // If already animating, do not start another animation
                if (GetIsAnimating(contentControl)) {
                    return;
                }

                // Mark as animating
                SetIsAnimating(contentControl, true);

                // Create animation for background fade-out
                Animation backgroundAnimation = CreateAnimation(TemplatedControl.BackgroundProperty, HighlightingConverter.GetHighlightBackgroundBrush(),
                    HighlightingConverter.GetDefaultBackgroundBrush());

                // Start background animation
                Task backgroundTask = backgroundAnimation.RunAsync(contentControl);

                // If foreground highlighting is enabled, animate it too
                if (highlightForeground) {
                    Animation foregroundAnimation = CreateAnimation(TemplatedControl.ForegroundProperty, HighlightingConverter.GetHighlightForegroundBrush(),
                        HighlightingConverter.GetDefaultForegroundBrush());

                    // Start foreground animation
                    Task foregroundTask = foregroundAnimation.RunAsync(contentControl);

                    // When both complete, clear animating flag on UI thread
                    Task.WhenAll(new Task[] { backgroundTask, foregroundTask }).ContinueWith(_ => {
                        Dispatcher.UIThread.Post(() => SetIsAnimating(contentControl, false));
                    });
                } else {
                    // Only background animation running
                    backgroundTask.ContinueWith(_ => {
                        Dispatcher.UIThread.Post(() => SetIsAnimating(contentControl, false));
                    });
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

                // Clear animating flag when set directly
                SetIsAnimating(panel, false);
            } else if (e.OldValue.Value) {
                // If already animating, do not start another animation
                if (GetIsAnimating(panel)) {
                    return;
                }

                // Mark as animating
                SetIsAnimating(panel, true);

                // Create animation for background fade-out
                Animation backgroundAnimation = CreateAnimation(Panel.BackgroundProperty, HighlightingConverter.GetHighlightBackgroundBrush(), HighlightingConverter.GetDefaultBackgroundBrush());

                // Start background animation
                Task backgroundTask = backgroundAnimation.RunAsync(panel);

                // When complete, clear animating flag on UI thread
                backgroundTask.ContinueWith(_ => {
                    Dispatcher.UIThread.Post(() => SetIsAnimating(panel, false));
                });
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