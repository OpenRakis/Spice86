using System;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.UpDock.Controls;

namespace Avalonia.UpDock;

internal class DockableTabWindow : Window, IDraggedOutTabHolder
{
    public Size TabContentSize { get; private set; }
    public Size TabItemSize { get; private set; }
    public Size TabControlSize { get; private set; }

    private record DragInfo(Point Offset);

    private IBrush? _tabBackground;
    private readonly IBrush? _tabItemBackground;
    private readonly IPen _borderPen = new Pen(Brushes.Gray);
    private readonly TabItem _tabItem;
    private readonly Size _contentSize;
    private readonly HookedTabControl _tabControl;
    private readonly Button _minimizeBtn;

    private DragInfo? _dragInfo = null;

    public event EventHandler<PointerEventArgs>? Dragging;
    public event EventHandler<PointerEventArgs>? DragEnd;

    TabItem IDraggedOutTabHolder.RetrieveTabItem()
    {
        _tabItem.PointerPressed -= TabItem_PointerPressed;
        _tabItem.PointerMoved -= TabItem_PointerMoved;
        _tabItem.PointerReleased -= TabItem_PointerReleased;
        _tabItem.PointerCaptureLost -= TabItem_PointerCaptureLost;

        _tabControl.Items.Clear();

        if (_tabItem is ClosableTabItem closable)
            closable.Closed -= TabItem_Closed;

        _tabItem.Background = _tabItemBackground;

        Close();
        return _tabItem;
    }
    
    private readonly TabBarDragHandler _tabBarDragHandler;

    public DockableTabWindow(TabItem tabItem, Size contentSize)
    {
#if DEBUG
        this.AttachDevTools();
#endif
        
        _tabItem = tabItem;
        _contentSize = contentSize;
        _tabItem.PointerPressed += TabItem_PointerPressed;
        _tabItem.PointerMoved += TabItem_PointerMoved;
        _tabItem.PointerReleased += TabItem_PointerReleased;
        _tabItem.PointerCaptureLost += TabItem_PointerCaptureLost;

        if (_tabItem is ClosableTabItem closable)
            closable.Closed += TabItem_Closed;

        _tabItemBackground = tabItem.Background;

        // this is a mess but aligning the minimize button with the tab item
        // in a way that doesn't look weird is a pain in the ...
        TextBlock sizeDummy;
        var minimizeBtnPanel = new Panel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            [Grid.ColumnProperty] = 1,
            Children =
            {
                new Panel
                {
                    [!WidthProperty] = _tabItem[!MinHeightProperty],
                    [!HeightProperty] = _tabItem[!MinHeightProperty]
                },
                (_minimizeBtn = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    MinHeight = 0,
                    Padding = new Thickness(0),
                    [!MarginProperty] = tabItem[!PaddingProperty],
                    [!FontSizeProperty] = tabItem[!FontSizeProperty],
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        FlowDirection = FlowDirection.RightToLeft,
                        Children =
                        {
                            new MinimizeSymbol(),
                            (sizeDummy = new TextBlock
                            {
                                [!FontSizeProperty] = tabItem[!FontSizeProperty]
                            })
                        },
                        [!MarginProperty] = sizeDummy.GetObservable(BoundsProperty)
                            .WithLatestFrom(tabItem.GetObservable(FontSizeProperty), (x, y) => (x.Height - y) / 2)
                            .Select(x=> new Thickness(x, 0, x, 0))
                            .ToBinding()
                    }
                        
                })
            }
        };

        _tabControl = new HookedTabControl
        {
            Background = Brushes.Transparent, //just to be safe
            [Grid.ColumnSpanProperty] = 2,
            [Grid.RowSpanProperty] = 2
        };
        
        var tabItemBoundsObservable = _tabItem.GetObservable(BoundsProperty);
        var gridLayout = new Grid
        {
            ColumnDefinitions = [
                new ColumnDefinition
                { 
                    [!ColumnDefinition.WidthProperty] = tabItemBoundsObservable
                        .Select(b => new GridLength(b.Width, GridUnitType.Pixel)).ToBinding()
                },
                new ColumnDefinition{ Width = new GridLength(1, GridUnitType.Star) }
            ],
            RowDefinitions = [
                new RowDefinition
                { 
                    [!RowDefinition.HeightProperty] = tabItemBoundsObservable
                        .Select(b => new GridLength(b.Height, GridUnitType.Pixel)).ToBinding()
                },
                new RowDefinition{ Height = new GridLength(1, GridUnitType.Star) }
            ],
            Children =
            {
                _tabControl,
                minimizeBtnPanel
            }
        };
        
        _tabBarDragHandler = new TabBarDragHandler(this, _tabControl);
        _tabControl.Items.Add(tabItem);
        

        SizeToContent = SizeToContent.WidthAndHeight;
        Content = gridLayout;

        _tabControl.LayoutUpdated += TabControl_LayoutUpdated;
        _tabControl.Padding = new Thickness(0);
        _minimizeBtn.Click += MinimizeBtn_Click;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _tabBarDragHandler.UnRegister();
        _minimizeBtn.Click -= MinimizeBtn_Click;
    }

    private class TabBarDragHandler
    {
        private readonly DockableTabWindow _dockableTabWindow;
        private readonly HookedTabControl _tabControl;

        public TabBarDragHandler(DockableTabWindow dockableTabWindow, HookedTabControl tabControl)
        {
            _dockableTabWindow = dockableTabWindow;
            _tabControl = tabControl;
            
            _tabControl.PointerPressed += TabControl_PointerPressed;
            _tabControl.PointerMoved += TabControl_PointerMoved;
            _tabControl.PointerReleased += TabControl_PointerReleased;
        }

        public void UnRegister()
        {
            _tabControl.PointerPressed -= TabControl_PointerPressed;
            _tabControl.PointerMoved -= TabControl_PointerMoved;
            _tabControl.PointerReleased -= TabControl_PointerReleased;
        }
        
        private bool _isDragging;
        private PixelPoint _previousPoint;

        private void TabControl_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (_tabControl.TabBarPresenter == null)
                return;
            
            if (!_tabControl.TabBarPresenter.Bounds.Contains(e.GetPosition(_tabControl)))
                return;
            
            _isDragging = true;
            _previousPoint = _dockableTabWindow.PointToScreen(e.GetPosition(_dockableTabWindow));
        }

        private void TabControl_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_isDragging)
                return;
            var currentPoint = _dockableTabWindow.PointToScreen(e.GetPosition(_dockableTabWindow));
            _dockableTabWindow.Position += currentPoint - _previousPoint;
            _previousPoint = currentPoint;
        }

        private void TabControl_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            _isDragging = false;
        }
    }

    private void TabControl_LayoutUpdated(object? sender, EventArgs e)
    {
        if (_tabControl.Bounds.Size == default)
            return;

        var presenter = _tabControl.ContentPresenter;
        if (presenter != null)
        {
            var extraWidth = _tabControl.Bounds.Width - presenter.Bounds.Width;
            var extraHeight = _tabControl.Bounds.Height - presenter.Bounds.Height;
            
            //probably not needed
            extraWidth += Width - _tabControl.Bounds.Width;
            extraHeight += Height - _tabControl.Bounds.Height;

            var newWidth = Math.Max(Width, _contentSize.Width + extraWidth);
            var newHeight = Math.Max(Height, _contentSize.Height + extraHeight);

            SizeToContent = SizeToContent.Manual;

            Width = newWidth;
            Height = newHeight;
        }

        _tabControl.LayoutUpdated -= TabControl_LayoutUpdated;
    }

    protected override void ArrangeCore(Rect finalRect)
    {
        base.ArrangeCore(finalRect);
        TabContentSize = _tabControl.ContentPresenter?.Bounds.Size ?? _tabControl.Bounds.Size;
        TabItemSize = _tabItem.Bounds.Size;
        TabControlSize = _tabControl.Bounds.Size;
    }

    private bool _isTabItemClosed = false;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        if (!_tabControl.Items.Contains(_tabItem))
            return; //the tabItem is not part of the window anymore

        if (_tabItem is not ClosableTabItem closable)
        {
            e.Cancel = true;
            return;
        }

        if (!_isTabItemClosed)
        {
            e.Cancel = true;
            closable.Close();
        }
    }

    private void TabItem_Closed(object? sender, RoutedEventArgs e)
    {
        _isTabItemClosed = true;
        Close();
    }

    private void TabItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
            return;
        OnDragStart(e);
    }

    private void TabItem_PointerMoved(object? sender, PointerEventArgs e) => OnDragging(e);
    private void TabItem_PointerReleased(object? sender, PointerEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
            return;
        OnDragEnd(e);
    }

    private void TabItem_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => OnCaptureLost(e);

    private void MinimizeBtn_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }
    
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != SystemDecorationsProperty || _tabBackground == null)
            return;

        if (SystemDecorations == SystemDecorations.None)
        {
            Background = null;
        }
        else
            Background = _tabBackground;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _tabBackground = Background;
        _tabItem.Background = Background;
        Background = null;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        Point topLeft = _tabItem.TranslatePoint(new Point(0, 0), this)!.Value;
        Point bottomRight = _tabItem.TranslatePoint(new Point(_tabItem.Bounds.Width, _tabItem.Bounds.Height), this)!.Value;

        var rect = Bounds.WithY(bottomRight.Y).WithHeight(Bounds.Height - bottomRight.Y);

        context.FillRectangle(_tabBackground!, new Rect(topLeft, bottomRight));
        context.FillRectangle(_tabBackground!, rect);
        context.DrawRectangle(_borderPen, rect);
        base.Render(context);
    }

    private PointerEventArgs? _lastPointerEvent = null;

    public void OnDragStart(PointerEventArgs e)
    {
        ExtendClientAreaToDecorationsHint = false;
        SystemDecorations = SystemDecorations.None;
        _minimizeBtn.IsVisible = false;
        _dragInfo = new(e.GetPosition(this));
        _lastPointerEvent = e;
    }

    public void OnDragEnd(PointerEventArgs e)
    {
        _dragInfo = null;
        DragEnd?.Invoke(this, e);
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
        _minimizeBtn.IsVisible = true;
        SystemDecorations = SystemDecorations.BorderOnly;
        _lastPointerEvent = null;
    }

    public void OnDragging(PointerEventArgs e)
    {
        if (_dragInfo == null)
            return;

        _lastPointerEvent = e;

        var offset = _dragInfo.Offset;

        Position = this.PointToScreen(e.GetPosition(this) - offset);
        Dragging?.Invoke(this, e);
    }

    public void OnCaptureLost(PointerCaptureLostEventArgs e)
    {
        if (_lastPointerEvent != null)
            OnDragEnd(_lastPointerEvent);
    }
    
    private class MinimizeSymbol : Control
    {
        private Geometry? _minusGeometry;
    
        public static readonly StyledProperty<double> FontSizeProperty =
            TextElement.FontSizeProperty.AddOwner<MinimizeSymbol>();
    
        public static readonly StyledProperty<IBrush?> ForegroundProperty =
            TextElement.ForegroundProperty.AddOwner<MinimizeSymbol>();
    
        public double FontSize
        {
            get => GetValue(FontSizeProperty);
            set => SetValue(FontSizeProperty, value);
        }
    
        public IBrush? Foreground
        {
            get => GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property != FontSizeProperty) 
                return;
        
            double thickness = Math.Max(FontSize * 0.1, 2);
            double radius = FontSize * 0.35;

            _minusGeometry = new RectangleGeometry(new Rect(
                -radius, -thickness/2, 
                radius * 2, thickness
            ), thickness/2, thickness/2);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(FontSize, FontSize);
        }

        public override void Render(DrawingContext context)
        {
            var center = Bounds.WithX(0).WithY(0).Center;

            using (context.PushTransform(Matrix.CreateTranslation(center)))
            {
                using (context.PushOpacity(IsPointerOver ? 1 : 0.6))
                {
                    context.DrawGeometry(Foreground, null, _minusGeometry!);
                }
            }
        }
    }

    private class HookedTabControl : TabControl
    {
        public ContentPresenter? ContentPresenter { get; private set; }
        public ItemsPresenter? TabBarPresenter { get; private set; }
        protected override Type StyleKeyOverride => typeof(TabControl);

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);
            ContentPresenter = e.NameScope.Find<ContentPresenter>("PART_SelectedContentHost");
            TabBarPresenter = e.NameScope.Find<ItemsPresenter>("PART_ItemsPresenter");
        }
    }
}