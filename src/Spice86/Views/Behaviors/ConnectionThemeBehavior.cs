namespace Spice86.Views.Behaviors;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Reactive;
using Avalonia.Styling;

using AvaloniaGraphControl;

using Spice86.ViewModels;
using Spice86.Views.Converters;

/// <summary>
/// Provides theme-aware coloring for <see cref="Connection"/> controls based on
/// the <see cref="CfgEdgeType"/> carried in the edge label.
/// </summary>
public static class ConnectionThemeBehavior {
    private static readonly ConditionalWeakTable<Connection, WeakEventHandler> _eventHandlers = new();

    public static readonly AttachedProperty<bool> EnableThemingProperty =
        AvaloniaProperty.RegisterAttached<Connection, bool>(
            "EnableTheming",
            typeof(ConnectionThemeBehavior),
            defaultValue: false);

    static ConnectionThemeBehavior() {
        EnableThemingProperty.Changed.Subscribe(
            new AnonymousObserver<AvaloniaPropertyChangedEventArgs<bool>>(OnEnableThemingChanged));
    }

    public static void SetEnableTheming(Control element, bool value) =>
        element.SetValue(EnableThemingProperty, value);

    public static bool GetEnableTheming(Control element) =>
        element.GetValue(EnableThemingProperty);

    private static void OnEnableThemingChanged(AvaloniaPropertyChangedEventArgs<bool> e) {
        if (e.Sender is not Connection connection) {
            return;
        }

        Application? application = Application.Current;
        if (application is null) {
            return;
        }

        if (_eventHandlers.TryGetValue(connection, out WeakEventHandler? existingHandler)) {
            existingHandler.Unsubscribe();
            _eventHandlers.Remove(connection);
        }

        if (e.NewValue.Value) {
            WeakEventHandler handler = new(connection, application);
            _eventHandlers.Add(connection, handler);
            handler.Subscribe();
            ApplyColor(connection);
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

    private static void ApplyColor(Connection connection) {
        CfgEdgeType edgeType = CfgEdgeType.Normal;

        if (connection.DataContext is Edge edge && edge.Label is CfgGraphEdgeLabel label) {
            edgeType = label.EdgeType;
        }

        if (EdgeColors.TryGetValue(edgeType, out (string ResourceKey, IBrush Fallback) entry)) {
            connection.Brush = ConverterUtilities.GetResourceBrush(entry.ResourceKey, entry.Fallback);
        }
    }

    private sealed class WeakEventHandler {
        private readonly WeakReference<Connection> _weakReference;
        private readonly Application _application;
        private readonly EventHandler _themeChangedHandler;
        private readonly EventHandler<global::Avalonia.Interactivity.RoutedEventArgs> _unloadedHandler;
        private readonly EventHandler _dataContextChangedHandler;

        public WeakEventHandler(Connection connection, Application application) {
            _weakReference = new WeakReference<Connection>(connection);
            _application = application;
            _themeChangedHandler = OnThemeChanged;
            _unloadedHandler = OnUnloaded;
            _dataContextChangedHandler = OnDataContextChanged;
        }

        public void Subscribe() {
            if (_weakReference.TryGetTarget(out Connection? connection)) {
                _application.ActualThemeVariantChanged += _themeChangedHandler;
                connection.Unloaded += _unloadedHandler;
                connection.DataContextChanged += _dataContextChangedHandler;
            }
        }

        public void Unsubscribe() {
            _application.ActualThemeVariantChanged -= _themeChangedHandler;
            if (_weakReference.TryGetTarget(out Connection? connection)) {
                connection.Unloaded -= _unloadedHandler;
                connection.DataContextChanged -= _dataContextChangedHandler;
            }
        }

        private void OnThemeChanged(object? sender, EventArgs e) {
            if (_weakReference.TryGetTarget(out Connection? connection)) {
                ApplyColor(connection);
            }
        }

        private void OnDataContextChanged(object? sender, EventArgs e) {
            if (_weakReference.TryGetTarget(out Connection? connection)) {
                ApplyColor(connection);
            }
        }

        private void OnUnloaded(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) {
            Unsubscribe();
            if (sender is Connection connection) {
                _eventHandlers.Remove(connection);
            }
        }
    }
}
