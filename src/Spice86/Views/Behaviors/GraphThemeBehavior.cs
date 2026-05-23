namespace Spice86.Views.Behaviors;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Reactive;

using AvaloniaGraphControl;

using Spice86.ViewModels.DataModels;
using Spice86.ViewModels.Enums;
using Spice86.Views.Converters;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

/// <summary>
/// Shared theme behavior for AvaloniaGraphControl elements (TextSticker, Connection and edge-label TextBlock).
/// </summary>
public static class GraphThemeBehavior {
    private static readonly ConditionalWeakTable<Control, WeakEventHandler> _eventHandlers = new();

    public static readonly AttachedProperty<bool> EnableThemingProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "EnableTheming",
            typeof(GraphThemeBehavior),
            defaultValue: false);

    private static readonly Dictionary<CfgEdgeType, (string ResourceKey, IBrush Fallback)> EdgeColors = new() {
        { CfgEdgeType.Normal, ("CfgEdgeNormalBrush", new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90))) },
        { CfgEdgeType.Jump, ("CfgEdgeJumpBrush", new SolidColorBrush(Color.FromRgb(0xD6, 0x89, 0x10))) },
        { CfgEdgeType.Call, ("CfgEdgeCallBrush", new SolidColorBrush(Color.FromRgb(0x2E, 0x86, 0xC1))) },
        { CfgEdgeType.Return, ("CfgEdgeReturnBrush", new SolidColorBrush(Color.FromRgb(0x8E, 0x44, 0xAD))) },
        { CfgEdgeType.Selector, ("CfgEdgeSelectorBrush", new SolidColorBrush(Color.FromRgb(0x28, 0xB4, 0x63))) },
        { CfgEdgeType.CallToReturn, ("CfgEdgeCallToReturnBrush", new SolidColorBrush(Color.FromRgb(0x1A, 0xBC, 0x9C))) },
        { CfgEdgeType.CpuFault, ("CfgEdgeCpuFaultBrush", new SolidColorBrush(Color.FromRgb(0xCB, 0x43, 0x35))) },
        { CfgEdgeType.IsolatedNodeLoop, (string.Empty, Brushes.Transparent) },
    };

    static GraphThemeBehavior() {
        EnableThemingProperty.Changed.Subscribe(
            new AnonymousObserver<AvaloniaPropertyChangedEventArgs<bool>>(OnEnableThemingChanged));
    }

    public static void SetEnableTheming(Control element, bool value) =>
        element.SetValue(EnableThemingProperty, value);

    public static bool GetEnableTheming(Control element) =>
        element.GetValue(EnableThemingProperty);

    private static void OnEnableThemingChanged(AvaloniaPropertyChangedEventArgs<bool> e) {
        if (e.Sender is not Control control) {
            return;
        }

        Application? application = Application.Current;
        if (application is null) {
            return;
        }

        if (_eventHandlers.TryGetValue(control, out WeakEventHandler? existingHandler)) {
            existingHandler.Unsubscribe();
            _eventHandlers.Remove(control);
        }

        if (e.NewValue.Value) {
            WeakEventHandler handler = new(control, application);
            _eventHandlers.Add(control, handler);
            handler.Subscribe();
            ApplyTheme(control);
        }
    }

    private static void ApplyTheme(Control control) {
        if (control is Border border) {
            border.Background = HighlightingConverter.GetDefaultBackgroundBrush();
            border.BorderBrush = HighlightingConverter.GetDefaultForegroundBrush();
            border.BorderThickness = new Thickness(1.5);

            if (border.DataContext is DosGraphNode dosGraphNode && dosGraphNode.Kind == DosGraphNodeKind.Mcb) {
                border.BorderBrush = ConverterUtilities.GetResourceBrush(
                    "CfgEdgeSelectorBrush",
                    HighlightingConverter.GetDefaultForegroundBrush());
                border.BorderThickness = new Thickness(2.0);
            }

            if (border.Child is TextBlock nodeTextBlock) {
                nodeTextBlock.Foreground = HighlightingConverter.GetDefaultForegroundBrush();
            }
            return;
        }

        if (control is TextSticker textSticker) {
            textSticker.TextForeground = HighlightingConverter.GetDefaultForegroundBrush();
            textSticker.Background = HighlightingConverter.GetDefaultBackgroundBrush();
            return;
        }

        if (control is Connection connection) {
            CfgEdgeType edgeType = CfgEdgeType.Normal;
            if (connection.DataContext is Edge edge && edge.Label is CfgGraphEdgeLabel edgeLabel) {
                edgeType = edgeLabel.EdgeType;
            }

            if (EdgeColors.TryGetValue(edgeType, out (string ResourceKey, IBrush Fallback) entry)) {
                connection.Brush = ConverterUtilities.GetResourceBrush(entry.ResourceKey, entry.Fallback);
            }
            return;
        }

        if (control is TextBlock textBlock) {
            CfgEdgeType edgeType = CfgEdgeType.Normal;
            if (textBlock.DataContext is CfgGraphEdgeLabel edgeLabel) {
                edgeType = edgeLabel.EdgeType;
            }

            if (EdgeColors.TryGetValue(edgeType, out (string ResourceKey, IBrush Fallback) entry)) {
                textBlock.Foreground = ConverterUtilities.GetResourceBrush(entry.ResourceKey, entry.Fallback);
            }
        }
    }

    private sealed class WeakEventHandler {
        private readonly WeakReference<Control> _weakReference;
        private readonly Application _application;
        private readonly EventHandler _themeChangedHandler;
        private readonly EventHandler<global::Avalonia.Interactivity.RoutedEventArgs> _unloadedHandler;
        private readonly EventHandler _dataContextChangedHandler;

        public WeakEventHandler(Control control, Application application) {
            _weakReference = new WeakReference<Control>(control);
            _application = application;
            _themeChangedHandler = OnThemeChanged;
            _unloadedHandler = OnUnloaded;
            _dataContextChangedHandler = OnDataContextChanged;
        }

        public void Subscribe() {
            if (_weakReference.TryGetTarget(out Control? control)) {
                _application.ActualThemeVariantChanged += _themeChangedHandler;
                control.Unloaded += _unloadedHandler;
                control.DataContextChanged += _dataContextChangedHandler;
            }
        }

        public void Unsubscribe() {
            _application.ActualThemeVariantChanged -= _themeChangedHandler;
            if (_weakReference.TryGetTarget(out Control? control)) {
                control.Unloaded -= _unloadedHandler;
                control.DataContextChanged -= _dataContextChangedHandler;
            }
        }

        private void OnThemeChanged(object? sender, EventArgs e) {
            if (_weakReference.TryGetTarget(out Control? control)) {
                ApplyTheme(control);
            }
        }

        private void OnDataContextChanged(object? sender, EventArgs e) {
            if (_weakReference.TryGetTarget(out Control? control)) {
                ApplyTheme(control);
            }
        }

        private void OnUnloaded(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) {
            Unsubscribe();
            if (sender is Control control) {
                _eventHandlers.Remove(control);
            }
        }
    }
}