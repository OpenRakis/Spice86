namespace Spice86.Views.Behaviors;

using System;
using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Reactive;

using Spice86.ViewModels;
using Spice86.ViewModels.TextPresentation;
using Spice86.Views.Converters;

/// <summary>
/// Provides theme-aware styling for CFG graph node controls rendered inside
/// a <see cref="Border"/> / <see cref="TextBlock"/> DataTemplate, including
/// syntax-highlighted inlines and last-executed border highlighting.
/// </summary>
public static class CfgNodeThemeBehavior {
    private static readonly ConditionalWeakTable<Border, WeakEventHandler> _eventHandlers = new();

    public static readonly AttachedProperty<bool> EnableThemingProperty =
        AvaloniaProperty.RegisterAttached<Border, bool>(
            "EnableTheming",
            typeof(CfgNodeThemeBehavior),
            defaultValue: false);

    static CfgNodeThemeBehavior() {
        EnableThemingProperty.Changed.Subscribe(
            new AnonymousObserver<AvaloniaPropertyChangedEventArgs<bool>>(OnEnableThemingChanged));
    }

    public static void SetEnableTheming(Control element, bool value) =>
        element.SetValue(EnableThemingProperty, value);

    public static bool GetEnableTheming(Control element) =>
        element.GetValue(EnableThemingProperty);

    private static void OnEnableThemingChanged(AvaloniaPropertyChangedEventArgs<bool> e) {
        if (e.Sender is not Border border) {
            return;
        }

        Application? application = Application.Current;
        if (application is null) {
            return;
        }

        if (_eventHandlers.TryGetValue(border, out WeakEventHandler? existingHandler)) {
            existingHandler.Unsubscribe();
            _eventHandlers.Remove(border);
        }

        if (e.NewValue.Value) {
            WeakEventHandler handler = new(border, application);
            _eventHandlers.Add(border, handler);
            handler.Subscribe();
            ApplyTheme(border);
        }
    }

    private static void ApplyTheme(Border border) {
        // Apply theme-aware background and border
        border.Background = HighlightingConverter.GetDefaultBackgroundBrush();

        bool isLastExecuted = border.DataContext is CfgGraphNode { IsLastExecuted: true };
        if (isLastExecuted) {
            IBrush lastExecutedBrush = ConverterUtilities.GetResourceBrush(
                "CfgNodeLastExecutedBorderBrush",
                new SolidColorBrush(Color.FromRgb(0xCB, 0x43, 0x35)));
            border.BorderBrush = lastExecutedBrush;
            border.BorderThickness = new Thickness(2.5);
        } else {
            border.BorderBrush = HighlightingConverter.GetDefaultForegroundBrush();
            border.BorderThickness = new Thickness(1.5);
        }

        // Apply syntax-highlighted inlines to the child TextBlock
        if (border.Child is TextBlock textBlock && border.DataContext is CfgGraphNode node) {
            ApplyFormattedSegments(textBlock, node.Segments);
        }
    }

    private static void ApplyFormattedSegments(TextBlock textBlock, List<FormattedTextSegment> segments) {
        textBlock.Inlines?.Clear();
        var inlines = new InlineCollection();
        foreach (FormattedTextSegment segment in segments) {
            var run = new Run { Text = segment.Text };
            run.Bind(TextElement.ForegroundProperty,
                FormatterTextKindToBrushConverter.GetDynamicResourceExtension(segment.Kind));
            inlines.Add(run);
        }
        textBlock.Inlines = inlines;
    }

    private sealed class WeakEventHandler {
        private readonly WeakReference<Border> _weakReference;
        private readonly Application _application;
        private readonly EventHandler _themeChangedHandler;
        private readonly EventHandler<global::Avalonia.Interactivity.RoutedEventArgs> _unloadedHandler;
        private readonly EventHandler _dataContextChangedHandler;

        public WeakEventHandler(Border border, Application application) {
            _weakReference = new WeakReference<Border>(border);
            _application = application;
            _themeChangedHandler = OnThemeChanged;
            _unloadedHandler = OnUnloaded;
            _dataContextChangedHandler = OnDataContextChanged;
        }

        public void Subscribe() {
            if (_weakReference.TryGetTarget(out Border? border)) {
                _application.ActualThemeVariantChanged += _themeChangedHandler;
                border.Unloaded += _unloadedHandler;
                border.DataContextChanged += _dataContextChangedHandler;
            }
        }

        public void Unsubscribe() {
            _application.ActualThemeVariantChanged -= _themeChangedHandler;
            if (_weakReference.TryGetTarget(out Border? border)) {
                border.Unloaded -= _unloadedHandler;
                border.DataContextChanged -= _dataContextChangedHandler;
            }
        }

        private void OnThemeChanged(object? sender, EventArgs e) {
            if (_weakReference.TryGetTarget(out Border? border)) {
                ApplyTheme(border);
            }
        }

        private void OnDataContextChanged(object? sender, EventArgs e) {
            if (_weakReference.TryGetTarget(out Border? border)) {
                ApplyTheme(border);
            }
        }

        private void OnUnloaded(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) {
            Unsubscribe();
            if (sender is Border border) {
                _eventHandlers.Remove(border);
            }
        }
    }
}
