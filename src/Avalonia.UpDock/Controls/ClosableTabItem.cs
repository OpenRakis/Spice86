using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Avalonia.UpDock.Controls;

public class ClosableTabItem : TabItem
{
    public event EventHandler<RoutedEventArgs>? Closed
    {
        add => AddHandler(TabClosedEvent, value);
        remove => RemoveHandler(TabClosedEvent, value);
    }

    /// <summary>
    /// When closed, tries to remove this <see cref="ClosableTabItem"/> from it's <see cref="TabControl"/>
    /// (if this property is set to <see langword="true"/>)
    /// <para>Default value is <see langword="true"/></para>
    /// </summary>
    public bool RemoveOnClose { get; set; } = true;
        
    /// <summary>
    /// Defines the <see cref="RemoveOnClose"/> property.
    /// </summary>
    public static readonly DirectProperty<ClosableTabItem, bool> RemoveOnCloseProperty = 
        AvaloniaProperty.RegisterDirect<ClosableTabItem, bool>(nameof(RemoveOnClose), 
            o=>o.RemoveOnClose, (o,v) => o.RemoveOnClose = v);

    /// <summary>
    /// Fired before the <see cref="ClosableTabItem"/> is closed.
    /// </summary>
    public event EventHandler<CancelEventArgs>? Closing;

    /// <summary>
    /// Routed event that can be used for global tracking of closed tabs
    /// </summary>
    public static readonly RoutedEvent<RoutedEventArgs> TabClosedEvent =
        RoutedEvent.Register<ClosableTabItem, RoutedEventArgs>("TabClosed", RoutingStrategies.Direct);

    private readonly Dictionary<IDataTemplate, IDataTemplate> _headerTemplateCache = [];

    private readonly IDataTemplate _defaultHeaderTemplate;

    public ClosableTabItem()
    {
        _defaultHeaderTemplate = CreateHeaderTemplateWithCloseButton(null);
        UpdateHeaderTemplate(null); //is there a better way?
    }

    protected virtual void OnClosing(CancelEventArgs e) => Closing?.Invoke(this, e);

    /// <summary>
    /// Closes this <see cref="ClosableTabItem"/>
    /// </summary>
    public void Close()
    {
        var e = new CancelEventArgs();

        OnClosing(e);

        if (e.Cancel)
            return;
            
        if (RemoveOnClose)
            RemoveFromParentTabControl();

        Content = "<This Tab Has been closed>";
        RaiseEvent(new RoutedEventArgs(TabClosedEvent));
    }

    private void RemoveFromParentTabControl()
    {
        if (Parent is not TabControl tabControl)
            return;
            
        tabControl.Items.Remove(this);
    }

    protected override Type StyleKeyOverride => typeof(TabItem);

    private bool _isSettingHeaderTemplate;
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (_isSettingHeaderTemplate)
            return;

        if (change.Property == HeaderTemplateProperty)
        {
            UpdateHeaderTemplate(HeaderTemplate);
        }
    }

    private void UpdateHeaderTemplate(IDataTemplate? dataTemplate)
    {
        _isSettingHeaderTemplate = true; //prevent indirect recursion

        if (dataTemplate == null)
        {
            HeaderTemplate = _defaultHeaderTemplate;
            _isSettingHeaderTemplate = false;
            return;
        }

        if (!_headerTemplateCache.ContainsKey(dataTemplate))
            _headerTemplateCache[dataTemplate] = CreateHeaderTemplateWithCloseButton(dataTemplate);

        HeaderTemplate = _headerTemplateCache[dataTemplate];

        _isSettingHeaderTemplate = false;
    }

    private IDataTemplate CreateHeaderTemplateWithCloseButton(IDataTemplate? inner)
    {
        return new FuncDataTemplate<object?>((content, _) =>
        {
            var panel = new StackPanel()
            {
                Orientation = Layout.Orientation.Horizontal,
                Spacing = 8
            };

            Control? output = null;

            if(inner != null && inner.Match(content))
                output = inner.Build(content);

            if (output != null)
                panel.Children.Add(output);
            else
            {
                panel.Children.Add(new ContentPresenter()
                {
                    Content = content,
                    VerticalAlignment = Layout.VerticalAlignment.Center,
                });
            }

            var closeButton = new CloseButton();

            closeButton.Click += (_, _) => Close();

            panel.Children.Add(closeButton);

            return panel;
        });
    }
    
    private class CloseButton : ToggleButton
    {
        protected override Type StyleKeyOverride => typeof(TextBlock);

        private Geometry? _crossGeometry;
        private Geometry? _circleGeometry;

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == FontSizeProperty)
            {
                double radius = FontSize / 2;

                _circleGeometry = new EllipseGeometry(
                    new Rect(-radius, -radius, radius * 2, radius * 2));

                _crossGeometry = CreateFilledCross(radius * 1.1f, thickness: 2);

            }
        }

        private Geometry CreateFilledCross(double radius, double thickness)
        {
            List<Point> points = [];

            for (var i = 0; i < 4; i++)
            {
                var mtx = Matrix.CreateRotation((Math.Tau * 0.25) * (i + 0.5));

                points.Add(new Point(-thickness / 2, -radius).Transform(mtx));
                points.Add(new Point(thickness / 2, -radius).Transform(mtx));
                points.Add(new Point(thickness / 2, -thickness / 2).Transform(mtx));
            }

            return new PolylineGeometry(points, true);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(FontSize, FontSize);
        }
        protected override void OnPointerEntered(PointerEventArgs e)
        {
            base.OnPointerEntered(e);
            InvalidateVisual();
        }
        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            var center = Bounds.WithX(0).WithY(0).Center;

            using (context.PushTransform(Matrix.CreateTranslation(center)))
            {
                using (context.PushOpacity(IsPointerOver ? 0.2 : 0.0))
                {
                    context.DrawGeometry(Foreground, null, _circleGeometry!);
                }

                using (context.PushOpacity(IsPointerOver ? 1 : 0.6))
                {
                    context.DrawGeometry(Foreground, null, _crossGeometry!);
                }
            }
        }
    }
}