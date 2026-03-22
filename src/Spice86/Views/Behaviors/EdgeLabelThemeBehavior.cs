namespace Spice86.Views.Behaviors;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Reactive;

using Spice86.ViewModels;
using Spice86.Views.Converters;

/// <summary>
/// Provides theme-aware foreground coloring for edge label <see cref="TextBlock"/> controls,
/// matching the connection color for the corresponding <see cref="CfgEdgeType"/>.
/// </summary>
public static class EdgeLabelThemeBehavior {
    private static readonly ConditionalWeakTable<TextBlock, WeakEventHandler> _eventHandlers = new();

    public static readonly AttachedProperty<bool> EnableThemingProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, bool>(
            "EnableTheming",
            typeof(EdgeLabelThemeBehavior),
            defaultValue: false);

    static EdgeLabelThemeBehavior() {
        EnableThemingProperty.Changed.Subscribe(
            new AnonymousObserver<AvaloniaPropertyChangedEventArgs<bool>>(OnEnableThemingChanged));
    }

    public static void SetEnableTheming(Control element, bool value) =>
        element.SetValue(EnableThemingProperty, value);

    public static bool GetEnableTheming(Control element) =>
        element.GetValue(EnableThemingProperty);

    private static void OnEnableThemingChanged(AvaloniaPropertyChangedEventArgs<bool> e) {
        if (e.Sender is not TextBlock textBlock) {
            return;
        }

        Application? application = Application.Current;
        if (application is null) {
            return;
        }

        if (_eventHandlers.TryGetValue(textBlock, out WeakEventHandler? existingHandler)) {
            existingHandler.Unsubscribe();
            _eventHandlers.Remove(textBlock);
        }

        if (e.NewValue.Value) {
            WeakEventHandler handler = new(textBlock, application);
            _eventHandlers.Add(textBlock, handler);
            handler.Subscribe();
            ApplyColor(textBlock);
        }
    }

    private static readonly Dictionary<CfgEdgeType, (string ResourceKey, IBrush Fallback)> EdgeColors = new() {
        { CfgEdgeType.Normal, ("CfgEdgeNormalBrush", new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90))) },
        { CfgEdgeType.Jump, ("CfgEdgeJumpBrush", new SolidColorBrush(Color.FromRgb(0xD6, 0x89, 0x10))) },
        { CfgEdgeType.Call, ("CfgEdgeCallBrush", new SolidColorBrush(Color.FromRgb(0x2E, 0x86, 0xC1))) },
        { CfgEdgeType.Return, ("CfgEdgeReturnBrush", new SolidColorBrush(Color.FromRgb(0x8E, 0x44, 0xAD))) },
        { CfgEdgeType.Selector, ("CfgEdgeSelectorBrush", new SolidColorBrush(Color.FromRgb(0x28, 0xB4, 0x63))) },
        { CfgEdgeType.CallToReturn, ("CfgEdgeCallToReturnBrush", new SolidColorBrush(Color.FromRgb(0x1A, 0xBC, 0x9C))) },
        { CfgEdgeType.CpuFault, ("CfgEdgeCpuFaultBrush", new SolidColorBrush(Color.FromRgb(0xCB, 0x43, 0x35))) },
    };

    private static void ApplyColor(TextBlock textBlock) {
        CfgEdgeType edgeType = CfgEdgeType.Normal;

        if (textBlock.DataContext is CfgGraphEdgeLabel label) {
            edgeType = label.EdgeType;
        }

        if (EdgeColors.TryGetValue(edgeType, out (string ResourceKey, IBrush Fallback) entry)) {
            textBlock.Foreground = ConverterUtilities.GetResourceBrush(entry.ResourceKey, entry.Fallback);
        }
    }

    private sealed class WeakEventHandler {
        private readonly WeakReference<TextBlock> _weakReference;
        private readonly Application _application;
        private readonly EventHandler _themeChangedHandler;
        private readonly EventHandler<global::Avalonia.Interactivity.RoutedEventArgs> _unloadedHandler;
        private readonly EventHandler _dataContextChangedHandler;

        public WeakEventHandler(TextBlock textBlock, Application application) {
            _weakReference = new WeakReference<TextBlock>(textBlock);
            _application = application;
            _themeChangedHandler = OnThemeChanged;
            _unloadedHandler = OnUnloaded;
            _dataContextChangedHandler = OnDataContextChanged;
        }

        public void Subscribe() {
            if (_weakReference.TryGetTarget(out TextBlock? textBlock)) {
                _application.ActualThemeVariantChanged += _themeChangedHandler;
                textBlock.Unloaded += _unloadedHandler;
                textBlock.DataContextChanged += _dataContextChangedHandler;
            }
        }

        public void Unsubscribe() {
            _application.ActualThemeVariantChanged -= _themeChangedHandler;
            if (_weakReference.TryGetTarget(out TextBlock? textBlock)) {
                textBlock.Unloaded -= _unloadedHandler;
                textBlock.DataContextChanged -= _dataContextChangedHandler;
            }
        }

        private void OnThemeChanged(object? sender, EventArgs e) {
            if (_weakReference.TryGetTarget(out TextBlock? textBlock)) {
                ApplyColor(textBlock);
            }
        }

        private void OnDataContextChanged(object? sender, EventArgs e) {
            if (_weakReference.TryGetTarget(out TextBlock? textBlock)) {
                ApplyColor(textBlock);
            }
        }

        private void OnUnloaded(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) {
            Unsubscribe();
            if (sender is TextBlock textBlock) {
                _eventHandlers.Remove(textBlock);
            }
        }
    }
}
