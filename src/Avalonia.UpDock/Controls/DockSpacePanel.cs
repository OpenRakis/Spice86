using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using LIST_MODIFY_HANDLER = System.Collections.Specialized.NotifyCollectionChangedEventHandler;

namespace Avalonia.UpDock.Controls;
using UI_ELEM = DockingOverlay<DockSpacePanel.DockNode>.DockUIElement;

/// <summary>
/// A <see cref="DockPanel"/> that sets up all it's child elements as a virtual tree of nested docking areas
/// (with <see cref="TabControl"/>s as root elements) which 
/// </summary>
public class DockSpacePanel : ResizeDockPanel, IDockSpaceTree<DockSpacePanel.DockNode>
{
    public record struct DockNode
    {
        internal readonly Control ControlRef;
        
        internal static DockNode Of(Control control) => new(control);

        private DockNode(Control dockNodeControl)
        {
            ControlRef = dockNodeControl;
        }
    }
    //this class has a LOT of responsibilities, that's why each responsibility has its own #region
    
    #region Style Properties
    public static StyledProperty<double> DockIndicatorFieldSizeProperty { get; } =
        AvaloniaProperty.Register<DockSpacePanel, double>(nameof(DockIndicatorFieldSize), 40);

    public static StyledProperty<double> DockIndicatorFieldSpacingProperty { get; } =
        AvaloniaProperty.Register<DockSpacePanel, double>(nameof(DockIndicatorFieldSpacing), 10);

    public static StyledProperty<float> DockIndicatorFieldCornerRadiusProperty { get; } =
        AvaloniaProperty.Register<DockSpacePanel, float>(nameof(DockIndicatorFieldCornerRadius), 5);

    public static StyledProperty<IBrush> DockIndicatorFieldFillProperty { get; } =
        AvaloniaProperty.Register<DockSpacePanel, IBrush>(nameof(DockIndicatorFieldFill), new SolidColorBrush(Colors.CornflowerBlue, 0.5));
    public static StyledProperty<IBrush> DockIndicatorFieldHoveredFillProperty { get; } =
        AvaloniaProperty.Register<DockSpacePanel, IBrush>(nameof(DockIndicatorFieldHoveredFill), new SolidColorBrush(Colors.CornflowerBlue));

    public static StyledProperty<IBrush> DockIndicatorFieldStrokeProperty { get; } =
        AvaloniaProperty.Register<DockSpacePanel, IBrush>(nameof(DockIndicatorFieldStroke), Brushes.CornflowerBlue);

    public static StyledProperty<double> DockIndicatorFieldStrokeThicknessProperty { get; } =
        AvaloniaProperty.Register<DockSpacePanel, double>(nameof(DockIndicatorFieldStrokeThickness), 1);

    public double DockIndicatorFieldSize
    {
        get => GetValue(DockIndicatorFieldSizeProperty); 
        set => SetValue(DockIndicatorFieldSizeProperty, value);
    }

    public double DockIndicatorFieldSpacing
    {
        get => GetValue(DockIndicatorFieldSpacingProperty); 
        set => SetValue(DockIndicatorFieldSpacingProperty, value);
    }

    public float DockIndicatorFieldCornerRadius
    {
        get => GetValue(DockIndicatorFieldCornerRadiusProperty); 
        set => SetValue(DockIndicatorFieldCornerRadiusProperty, value);
    }

    public IBrush DockIndicatorFieldFill
    {
        get => GetValue(DockIndicatorFieldFillProperty); 
        set => SetValue(DockIndicatorFieldFillProperty, value);
    }

    public IBrush DockIndicatorFieldHoveredFill
    {
        get => GetValue(DockIndicatorFieldHoveredFillProperty); 
        set => SetValue(DockIndicatorFieldHoveredFillProperty, value);
    }

    public IBrush DockIndicatorFieldStroke
    {
        get => GetValue(DockIndicatorFieldStrokeProperty); 
        set => SetValue(DockIndicatorFieldStrokeProperty, value);
    }

    public double DockIndicatorFieldStrokeThickness
    {
        get => GetValue(DockIndicatorFieldStrokeThicknessProperty); 
        set => SetValue(DockIndicatorFieldStrokeThicknessProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DockIndicatorFieldStrokeProperty ||
            change.Property == DockIndicatorFieldStrokeThicknessProperty)
        {
            _dockIndicatorStrokePen = new Pen(DockIndicatorFieldStroke, DockIndicatorFieldStrokeThickness);
            InvalidateVisual();
            return;
        }
        if (change.Property == DockIndicatorFieldHoveredFillProperty)
            InvalidateVisual();
    }
    
    private IPen _dockIndicatorStrokePen = new Pen(
        DockIndicatorFieldStrokeProperty.GetDefaultValue(typeof(DockSpacePanel)),
        DockIndicatorFieldStrokeThicknessProperty.GetDefaultValue(typeof(DockSpacePanel)));
    #endregion
    
    #region DockSpaceTree state query api (implements IDockSpaceTree<DockNode>)

    DockNode IDockSpaceTree<DockNode>.GetRootNode() => DockNode.Of(this);
    bool IDockSpaceTree<DockNode>.CanFill(DockNode node) => node.ControlRef is TabControl;

    bool IDockSpaceTree<DockNode>.CanSplit(DockNode node) => 
        node.ControlRef is not TabControl tabControl ||
        //we don't want to split empty TabControls as they are not supposed to be children of SplitPanel
        tabControl.Items.Any(x => x is not DummyTabItem);
    
    Rect IDockSpaceTree<DockNode>.GetDockAreaBounds(DockNode node) => this.GetBoundsOf(node.ControlRef);

    DockingLocations<DockNode> IDockSpaceTree<DockNode>.GetDockingLocations(DockNode node)
        => GetDockingLocations(node.ControlRef);

    private DockingLocations<DockNode> GetDockingLocations(Control dockSpaceControl)
    {
        if (dockSpaceControl.Parent is SplitPanel parentSplitPanel)
            return GetDockingLocations(dockSpaceControl, parentSplitPanel);
        
        if (dockSpaceControl.Parent != this)
            return new DockingLocations<DockNode>();

        if (dockSpaceControl == Children[^1]) //"Fill" Element
        {
            return DockingLocations<DockNode>.All(DockNode.Of(dockSpaceControl));
        }

        var dockingLocations = new DockingLocations<DockNode>();
        var node = DockNode.Of(dockSpaceControl);
        switch (GetDock(dockSpaceControl))
        {
            case Dock.Left:
                dockingLocations.Left = node; break;
            case Dock.Right:
                dockingLocations.Right = node; break;
            case Dock.Top:
                dockingLocations.Top = node; break;
            case Dock.Bottom:
                dockingLocations.Bottom = node; break;
            default:
                Debug.Fail("Invalid dock value");
                break;
        }
        return dockingLocations;
    }
    private DockingLocations<DockNode> GetDockingLocations(Control dockSpaceControl, SplitPanel parentSplitPanel)
    {
        bool isVertical = parentSplitPanel.Orientation == Orientation.Vertical;

        var parentDockingLocations = GetDockingLocations(parentSplitPanel);
        if (parentDockingLocations == DockingLocations<DockNode>.Empty)
            return DockingLocations<DockNode>.Empty;

        var node = DockNode.Of(parentSplitPanel);
        var dockingLocations = new DockingLocations<DockNode>();
        
        if (isVertical)
        {
            dockingLocations.Left = dockingLocations.Right = node;
            
            if (dockSpaceControl == parentSplitPanel.GetControlAtSlot(0))
                dockingLocations.Top = node;
            if (dockSpaceControl == parentSplitPanel.GetControlAtSlot(^1))
                dockingLocations.Bottom = node;
        }
        else
        {
            dockingLocations.Top = dockingLocations.Bottom = node;
            
            if (dockSpaceControl == parentSplitPanel.GetControlAtSlot(0))
                dockingLocations.Left = node;
            if (dockSpaceControl == parentSplitPanel.GetControlAtSlot(^1))
                dockingLocations.Right = node;
        }
        
        return dockingLocations;
    }

    void IDockSpaceTree<DockNode>.VisitDockingTreeNodes(Action<DockNode> visitor)
    {
        static void VisitSplitPanel(SplitPanel splitPanel, Action<DockNode> visitor)
        {
            foreach (var child in splitPanel.Children)
            {
                if (child is SplitPanel childSplitPanel)
                    VisitSplitPanel(childSplitPanel, visitor);
                else if (child is TabControl tabControl)
                    visitor(DockNode.Of(tabControl));
            }
        }
        
        foreach (var child in Children)
        {
            if (child is SplitPanel childSplitPanel)
                VisitSplitPanel(childSplitPanel, visitor);
            else if (child is TabControl tabControl)
                visitor(DockNode.Of(tabControl));
        }
    }
    bool IDockSpaceTree<DockNode>.HitTestTabItem(DockNode node, Point point, out int index, out Rect tabItemRect)
    {
        index = -1;
        tabItemRect = default;
        if (node.ControlRef is not TabControl tabControl)
            return false;

        for (int i = tabControl.Items.Count - 1; i >= 0; i--)
        {
            if (tabControl.Items[i] is not TabItem tabItem)
                continue;
            
            var bounds = this.GetBoundsOf(tabItem);
            if (!bounds.Contains(point))
                continue;

            index = i;
            tabItemRect = bounds;
            return true;
        }
        
        return false;
    }

    #endregion

    #region Register/Unregister Docking Tree Elements (SplitPanels and TabControls)
    private readonly Dictionary<TabControl, LIST_MODIFY_HANDLER> _registeredTabControls = [];
    private readonly Dictionary<SplitPanel, LIST_MODIFY_HANDLER> _registeredSplitPanels = [];

    private void RegisterSplitPanelForDocking(SplitPanel splitPanel)
    {
        Debug.Assert(!_registeredSplitPanels.ContainsKey(splitPanel));

        LIST_MODIFY_HANDLER handler = (_, e) => SplitPanel_ChildrenModified(splitPanel, e);
        splitPanel.Children.CollectionChanged += handler;
        _registeredSplitPanels[splitPanel] = handler;

        foreach (var child in splitPanel.Children)
        {
            switch (child)
            {
                case TabControl tabControl:
                    RegisterTabControlForDocking(tabControl);
                    break;
                case SplitPanel childSplitPanel:
                    RegisterSplitPanelForDocking(childSplitPanel);
                    break;
            }
        }
    }

    private void UnregisterSplitPanel(SplitPanel splitPanel)
    {
        if (_registeredSplitPanels.Remove(splitPanel, out var handler))
            splitPanel.Children.CollectionChanged -= handler;
        else
            throw new Exception("SplitPanel not registered");

        foreach (var child in splitPanel.Children)
        {
            switch (child)
            {
                case TabControl tabControl:
                    UnregisterTabControl(tabControl);
                    break;
                case SplitPanel childSplitPanel:
                    UnregisterSplitPanel(childSplitPanel);
                    break;
            }
        }
    }

    private void RegisterTabControlForDocking(TabControl tabControl)
    {
        Debug.Assert(!_registeredTabControls.ContainsKey(tabControl));

        LIST_MODIFY_HANDLER handler = (_, e) => TabControl_ItemsModified(tabControl, e);
        tabControl.Items.CollectionChanged += handler;
        _registeredTabControls[tabControl] = handler;

        if (tabControl is RearrangeTabControl rearrangeTabControl)
            rearrangeTabControl.RegisterDraggedOutTabHandler(TabControl_DraggedOutTab);
    }

    private void UnregisterTabControl(TabControl tabControl)
    {
        if (_registeredTabControls.Remove(tabControl, out var handler))
            tabControl.Items.CollectionChanged -= handler;
        else
            throw new Exception("TabControl not registered");

        if (tabControl is RearrangeTabControl rearrangeTabControl)
            rearrangeTabControl.UnregisterDraggedOutTabHandler();
    }
    #endregion

    #region Handle Tree Modifications (Children/Items)
    private readonly HashSet<SplitPanel> _ignoreModified = [];
    
    /// <summary>
    /// Ensures that after a modification there are still no SplitPanels with less than 2 Children
    /// unless it's the root
    /// </summary>
    private void SplitPanel_ChildrenModified(SplitPanel splitPanel, NotifyCollectionChangedEventArgs e)
    {
        if (_ignoreModified.Contains(splitPanel))
            return;

        HandleChildrenModified(e);

        if (e.Action != NotifyCollectionChangedAction.Remove)
            return;

        if (splitPanel.Parent is not Panel parentPanel)
            return;

        int indexInParent = parentPanel.Children.IndexOf(splitPanel);

        if (splitPanel.Children.Count == 1)
        {
            var child = splitPanel.Children[0];

            //panel is still part of the UI Tree and as such can trigger a cascade of unwanted changes
            //and we can't remove it without triggering ChildrenModified
            //or replacing it with a Dummy Control so we have to:
            _ignoreModified.Add(splitPanel);

            if (child is TabControl childTabControl)
                UnregisterTabControl(childTabControl);
            else if (child is SplitPanel childSplitPanel)
                UnregisterSplitPanel(childSplitPanel);

            splitPanel.Children.Clear();
            _ignoreModified.Remove(splitPanel);

            parentPanel.Children[indexInParent] = child;
        }
        else if (splitPanel.Children.Count == 0)
            parentPanel.Children.RemoveAt(indexInParent);
    }

    /// <summary>
    /// Ensures that after a modification there are still no TabControls with no Items in the Dock Tree unless it's the last child of the DockingHost
    /// aka the "Fill" control
    /// </summary>
    private void TabControl_ItemsModified(TabControl tabControl, NotifyCollectionChangedEventArgs e)
    {
        if (tabControl.Items.Count > 0)
            return;

        if (tabControl.Parent is not Panel parent)
            return;
        
        // make sure we don't delete the tabControl if it's our "Fill" element
        // if we allow deleting the Fill element we can end up in a situation where the last remaining Child is a Docked
        // Control and therefore doesn't fill this DockPanel fully which causes this DockPanel as a whole to shrink
        if (parent == this && tabControl == Children[^1])
            return;

        // it's not supposed to happen but we better catch it
        if (e.OldItems?.Cast<object>().Any(x => x is DummyTabItem) == true &&
            e.OldItems.Count == 1)
            return;

        //tabControl has no tabs left and can be removed
        int indexInParent = parent.Children.IndexOf(tabControl);
        if (parent is SplitPanel parentSplitPanel)
        {
            if (indexInParent < parentSplitPanel.SlotCount)
                parentSplitPanel.RemoveSlot(indexInParent);
        }
        else
            parent.Children.RemoveAt(indexInParent);
        
    }
    
    protected override void ChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        base.ChildrenChanged(sender, e);
        HandleChildrenModified(e);
    }

    private void HandleChildrenModified(NotifyCollectionChangedEventArgs e)
    {
        foreach (var child in e.OldItems?.OfType<Control>() ?? [])
        {
            if (child is SplitPanel splitPanel)
                UnregisterSplitPanel(splitPanel);
            else if (child is TabControl tabControl)
                UnregisterTabControl(tabControl);
        }

        foreach (var child in e.NewItems?.OfType<Control>() ?? [])
        {
            if (child is SplitPanel splitPanel)
                RegisterSplitPanelForDocking(splitPanel);
            else if (child is TabControl tabControl)
                RegisterTabControlForDocking(tabControl);
        }
    }
    #endregion

    #region Create Dock Tree Nodes Savely
    /// <summary>
    /// Creates a <see cref="RearrangeTabControl"/> that has been set up for Docking
    /// </summary>
    private static RearrangeTabControl CreateTabControl(TabItem initialTabItem)
    {
        var tabControl = new RearrangeTabControl();
        tabControl.Items.Add(initialTabItem);
        return tabControl;
    }

    /// <summary>
    /// Creates and inserts a <see cref="SplitPanel"/> that has been setup for Docking
    /// <para>It does so in a way that no unwanted side effects are triggered</para>
    /// </summary>
    private static void InsertSplitPanel(Orientation orientation,
        (int fraction, Control child) slot1, (int fraction, Control child) slot2,
        Action<SplitPanel> insertAction)
    {
        var panel = new SplitPanel
        {
            Orientation = orientation,
            Fractions = new SplitFractions(slot1.fraction, slot2.fraction)
        };

        insertAction(panel);
        panel.Children.AddRange([slot1.child, slot2.child]);
    }

    /// <summary>
    /// Creates all necessary <see cref="SplitPanel"/>s and splits to visually split <paramref name="targetControl"/>
    /// in two and place <paramref name="controlToInsert"/> at the <paramref name="dock"/> position
    /// </summary>
    private static void ApplySplitDock(Control targetControl, Dock dock, Size dockSize, Control controlToInsert)
    {
        if (targetControl.Parent is not Panel parent)
            throw new InvalidOperationException();

        var splitOrientation = dock switch
        {
            Dock.Left or Dock.Right => Orientation.Horizontal,
            Dock.Top or Dock.Bottom => Orientation.Vertical,
            _ => throw null!
        };

        var slotSize = targetControl.Bounds.Size;

        (int insertSlotSize, int otherSlotSize) = dock switch
        {
            Dock.Left or Dock.Right => ((int)dockSize.Width, (int)(slotSize.Width - dockSize.Width)),
            Dock.Top or Dock.Bottom => ((int)dockSize.Height, (int)(slotSize.Height - dockSize.Height)),
            _ => throw null!
        };

        Action<SplitPanel> insertAction;

        if (parent is SplitPanel splitPanel)
        {
            int dropSlot = splitPanel.Children.IndexOf(targetControl);
            if (splitPanel.TrySplitSlot(dropSlot, (dock, insertSlotSize, controlToInsert), otherSlotSize))
                return;

            insertAction = panel => parent.Children[dropSlot] = panel;
        }
        else
        {
            insertAction = createdSplitPanel =>
            {
                var targetControlDock = GetDock(targetControl);
                if (targetControlDock != DockProperty.GetDefaultValue(typeof(Control)))
                    createdSplitPanel.SetValue(DockProperty, targetControlDock);

                parent.Children[parent.Children.IndexOf(targetControl)] = createdSplitPanel;
            };
        }

        if (dock is Dock.Left or Dock.Top)
        {
            InsertSplitPanel(splitOrientation,
                (insertSlotSize, controlToInsert), (otherSlotSize, targetControl),
                insertAction);
        }
        else
        {
            InsertSplitPanel(splitOrientation,
                (otherSlotSize, targetControl), (insertSlotSize, controlToInsert),
                insertAction);
        }
    }
    #endregion
    
    #region Handle dragged out tabs, overlay and tab drop (docking)
    
    private DockableTabWindow? _draggedDockTabWindow;
    private DockingOverlay<DockNode>? _overlay;
    private DockingOverlayWindow? _overlayWindow;
    
    //helper function
    private Window GetHostWindow()
    {
        if (VisualRoot is Window window)
            return window;

        throw new InvalidOperationException(
            $"This {nameof(DockSpacePanel)} is not part of a Window");
    }

    /// <summary>
    /// Creates a <see cref="DockableTabWindow"/> for the dragged out tab and hooks it up
    /// </summary>
    private void TabControl_DraggedOutTab(object? sender, PointerEventArgs e,
        TabItem tabItem, Point offset, Size contentSize)
    {
        var tabControl = (RearrangeTabControl)sender!;
        var hostWindow = GetHostWindow();

        var window = new DockableTabWindow(tabItem, contentSize)
        {
            Width = tabControl.Bounds.Width,
            Height = tabControl.Bounds.Height,
            SystemDecorations = SystemDecorations.None,
            Position = hostWindow.PointToScreen(e.GetPosition(hostWindow) + offset),
            DataContext = hostWindow.DataContext
        };

        window.Show(hostWindow);

        ChildWindowMoveHandler.Hookup(hostWindow, window);

        _draggedDockTabWindow = window;
        window.OnDragStart(e);
        window.Dragging += DockTabWindow_Dragging;
        window.DragEnd += DockTabWindow_DragEnd;
    }
    
    //Redirect mouse input to dragged Window
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _draggedDockTabWindow?.OnDragEnd(e);
        _draggedDockTabWindow = null;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _draggedDockTabWindow?.OnCaptureLost(e);
        _draggedDockTabWindow = null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _draggedDockTabWindow?.OnDragging(e);
    }

    /// <summary>
    /// Makes the dragged <see cref="DockableTabWindow"/> "interact" with the <see cref="DockingOverlay{TDockNode}"/>
    /// </summary>
    private void DockTabWindow_Dragging(object? sender, PointerEventArgs e)
    {
        _draggedDockTabWindow = (DockableTabWindow)sender!;

        if (_overlay == null)
        {
            _overlay = new DockingOverlay<DockNode>(this, new DockingOverlay<DockNode>.StyleParameters
            {
                FieldSize = DockIndicatorFieldSize,
                FieldSpacing = DockIndicatorFieldSpacing
            });
            _overlayWindow = new DockingOverlayWindow(this)
            {
                SystemDecorations = SystemDecorations.None,
                Background = null,
                Opacity = 0.5
            };

            _overlay.UIChanged += (_, _) => _overlayWindow?.InvalidateVisual();

            _overlay.AreaEntered += Overlay_AreaEntered;
            _overlay.AreaExited += Overlay_AreaExited;
            _overlay.TabDropped += Overlay_TabDropped;

            _overlayWindow.Show(GetHostWindow());
            _overlayWindow.Position = this.PointToScreen(new Point());
            _overlayWindow.Width = Bounds.Width;
            _overlayWindow.Height = Bounds.Height;
            _overlay.UpdateAreas();
        }

        _overlay.DragOver(e.GetPosition(this), _draggedDockTabWindow);
    }
    
    protected override void ArrangeCore(Rect finalRect)
    {
        base.ArrangeCore(finalRect);
        _overlay?.UpdateAreas();
    }

    /// <summary>
    /// Adds a <see cref="DummyTabItem"/> to the hovered <see cref="TabControl"/>
    /// as an indicator how adding a tab would affect the tab bar
    /// </summary>
    private void Overlay_AreaEntered(object? sender, DockingOverlay<DockNode>.AreaEnteredEventArgs e)
    {
        Debug.Assert(_draggedDockTabWindow != null);

        if (e.Node.ControlRef is not TabControl tabControl) 
            return;
        
        object? item = tabControl.Items.FirstOrDefault(x => x is DummyTabItem);

        if (item != null)
            tabControl.Items.Remove(item);

        tabControl.Items.Add(new DummyTabItem(this)
        {
            Width = _draggedDockTabWindow.TabItemSize.Width,
            Height = _draggedDockTabWindow.TabItemSize.Height,
            Opacity = 0.5
        });
    }
    
    /// <summary>
    /// Removes the <see cref="DummyTabItem"/> from the previously hovered <see cref="TabControl"/>
    /// </summary>
    private static void Overlay_AreaExited(object? sender, DockingOverlay<DockNode>.AreaExitedEventArgs e)
    {
        if (e.Node.ControlRef is not TabControl tabControl) 
            return;
        
        object? item = tabControl.Items.FirstOrDefault(x => x is DummyTabItem);

        if (item != null)
            tabControl.Items.Remove(item);
    }
    
    /// <summary>
    /// Makes the dropped <see cref="DockableTabWindow"/> "interact" with the <see cref="DockingOverlay{TDockNode}"/>
    /// </summary>
    private void DockTabWindow_DragEnd(object? sender, PointerEventArgs e)
    {
        _overlay?.DropTab(e.GetPosition(this), (DockableTabWindow)sender!); //Triggers Overlay_TabDropped
        _overlay = null;
        _overlayWindow?.Close();
        _overlayWindow = null;
    }

    /// <summary>
    /// Extracts the tab from it's <see cref="DockableTabWindow"/> and inserts it into the Dock Tree
    /// </summary>
    private void Overlay_TabDropped(object? sender, DockingOverlay<DockNode>.TabDroppedEventArgs e)
    {
        var tabDropInfo = e.DropInfo;

        Debug.Assert(_overlay != null);
        _overlayWindow?.Close();
        _overlayWindow = null;
        _overlay = null;
        _draggedDockTabWindow = null;

        if (tabDropInfo.IsInsertTab(out var target, out int index))
        {
            if (target.Value.ControlRef is not TabControl tabControl)
            {
                Debug.Fail("Invalid dropTarget for control");
                return;
            }
            
            var tabItem = e.TabHolder.RetrieveTabItem();
            tabControl.Items.Insert(index, tabItem);
        }
        else if (tabDropInfo.IsFillControl(out target))
        {
            if (target.Value.ControlRef is not TabControl tabControl)
            {
                Debug.Fail("Invalid dropTarget for control");
                return;
            }

            var tabItem = e.TabHolder.RetrieveTabItem();
            tabControl.Items.Add(tabItem);
        }
        else if (tabDropInfo.IsSplitControl(out target, out var dock))
        {
            var dockNodeControl = target.Value.ControlRef;
            var tabItem = e.TabHolder.RetrieveTabItem();
            TabControl newTabControl = CreateTabControl(tabItem);
            var dockSize = IDockSpaceTree<DockNode>.CalculateDockRect(e.TabHolder.TabControlSize,
                new Rect(default, dockNodeControl.Bounds.Size), dock, true)
                .Size;

            ApplySplitDock(dockNodeControl, dock, dockSize, newTabControl);
        }
        else if (tabDropInfo.IsInsertNextTo(out target, out dock))
        {
            var tabItem = e.TabHolder.RetrieveTabItem();
            var dockLocations = GetDockingLocations(target.Value.ControlRef);
            if (!dockLocations.TryGetDockingLocation(dock, out target)) //override target to make sure it's valid
                throw new Exception("Layout has changed since tab has been dragged");

            TabControl newTabControl = CreateTabControl(tabItem);
            newTabControl.SetValue(DockProperty, dock);

            if (dock is Dock.Left or Dock.Right)
                newTabControl.Width = e.TabHolder.TabContentSize.Width;
            else
                newTabControl.Height = e.TabHolder.TabContentSize.Height;

            int insertIndex = Children.IndexOf(target.Value.ControlRef);
            Children.Insert(insertIndex, newTabControl);
        }
        else if (tabDropInfo.IsInsertOuter(out dock))
        {
            var tabItem = e.TabHolder.RetrieveTabItem();
            TabControl newTabControl = CreateTabControl(tabItem);
            newTabControl.SetValue(DockProperty, dock);

            if (dock is Dock.Left or Dock.Right)
                newTabControl.Width = e.TabHolder.TabContentSize.Width;
            else
                newTabControl.Height = e.TabHolder.TabContentSize.Height;
            Children.Insert(0, newTabControl);
        }
    }
    #endregion

    /// <summary>
    /// Draws the current <see cref="DockingOverlay{TDockNode}"/> on top of all other windows
    /// </summary>
    private class DockingOverlayWindow : Window
    {
        private readonly DockSpacePanel _dsp;

        public DockingOverlayWindow(DockSpacePanel dsp)
        {
            _dsp = dsp;
            Background = null;
            Topmost = true;
        }

        public override void Render(DrawingContext ctx)
        {
            if (_dsp._overlay == null)
            {
                Debug.Fail("Overlay window should already be closed");
                return;
            }
            
            var (_outerEdgesDockUI, hoveredAreaDockUI, dockedElementBounds) = _dsp._overlay.GetUI();
            
            if (hoveredAreaDockUI.HasValue)
            {
                var dockUI = hoveredAreaDockUI.Value;
                var hovered = dockUI.HoveredElement;
                ctx.DrawRectangle(_dsp._dockIndicatorStrokePen, dockUI.AreaBounds);

                double cornerRadius = _dsp.DockIndicatorFieldCornerRadius * dockUI.CornerRadiusScaling;
                

                //inner
                DrawDockControl(ctx, dockUI.LeftSplitRect, (0, .5), (0, 1), cornerRadius,
                         hovered == UI_ELEM.LeftSplitRect);
                DrawDockControl(ctx, dockUI.RightSplitRect, (.5, 1), (0, 1), cornerRadius,
                         hovered == UI_ELEM.RightSplitRect);
                DrawDockControl(ctx, dockUI.TopSplitRect, (0, 1), (0, .5), cornerRadius,
                         hovered == UI_ELEM.TopSplitRect);
                DrawDockControl(ctx, dockUI.BottomSplitRect, (0, 1), (.5, 1), cornerRadius,
                         hovered == UI_ELEM.BottomSplitRect);
                
                //center
                DrawDockControl(ctx, dockUI.CenterRect, (0, 1), (0, 1), cornerRadius,
                         hovered == UI_ELEM.CenterRect);

                //outer
                DrawDockControl(ctx, dockUI.LeftNeighborRect, (0, .5), (0, 1), cornerRadius,
                         hovered == UI_ELEM.LeftNeighborRect, isNeighborDock: true);
                DrawDockControl(ctx, dockUI.RightNeighborRect, (.5, 1), (0, 1), cornerRadius,
                         hovered == UI_ELEM.RightNeighborRect, isNeighborDock: true);
                DrawDockControl(ctx, dockUI.TopNeighborRect, (0, 1), (0, .5), cornerRadius,
                         hovered == UI_ELEM.TopNeighborRect, isNeighborDock: true);
                DrawDockControl(ctx, dockUI.BottomNeighborRect, (0, 1), (.5, 1), cornerRadius,
                         hovered == UI_ELEM.BottomNeighborRect, isNeighborDock: true);

                if (hovered == UI_ELEM.TabItemRect && dockUI.TabItemRect.HasValue)
                    ctx.FillRectangle(_dsp.DockIndicatorFieldFill, dockUI.TabItemRect.Value);
            }

            if (_outerEdgesDockUI.HasValue)
            {
                var dockUI = _outerEdgesDockUI.Value;
                var hovered = dockUI.HoveredElement;
                double cornerRadius = _dsp.DockIndicatorFieldCornerRadius * dockUI.CornerRadiusScaling;

                DrawDockControl(ctx, dockUI.LeftNeighborRect, (0, .5), (0, 1), cornerRadius, 
                    hovered == UI_ELEM.LeftNeighborRect, isNeighborDock: true);
                DrawDockControl(ctx, dockUI.RightNeighborRect, (.5, 1), (0, 1), cornerRadius, 
                    hovered == UI_ELEM.RightNeighborRect, isNeighborDock: true);
                DrawDockControl(ctx, dockUI.TopNeighborRect, (0, 1), (0, .5), cornerRadius, 
                    hovered == UI_ELEM.TopNeighborRect, isNeighborDock: true);
                DrawDockControl(ctx, dockUI.BottomNeighborRect, (0, 1), (.5, 1), cornerRadius, 
                    hovered == UI_ELEM.BottomNeighborRect, isNeighborDock: true);
            }
            
            if (dockedElementBounds != null)
                ctx.FillRectangle(_dsp.DockIndicatorFieldFill, dockedElementBounds.Value);
        }

        private void DrawDockControl(DrawingContext ctx, 
            Rect? rect, (double l, double r) lrPercent, (double t, double b) tbPercent, double cornerRadius,
            bool isHovered, bool isNeighborDock = false)
        {
            if (!rect.HasValue)
                return;
            
            static double Lerp(double a, double b, double t) => (1 - t) * a + t * b;
            double l = Lerp(rect.Value.Left, rect.Value.Right, lrPercent.l);
            double r = Lerp(rect.Value.Left, rect.Value.Right, lrPercent.r);
            double t = Lerp(rect.Value.Top, rect.Value.Bottom, tbPercent.t);
            double b = Lerp(rect.Value.Top, rect.Value.Bottom, tbPercent.b);
            var fillRect = new Rect(new Point(l, t), new Point(r, b));

            if (isHovered)
                ctx.FillRectangle(_dsp.DockIndicatorFieldHoveredFill, fillRect, (float)cornerRadius);
            else
                ctx.FillRectangle(_dsp.DockIndicatorFieldFill, fillRect, (float)cornerRadius);

            if (isNeighborDock)
                ctx.DrawRectangle(_dsp._dockIndicatorStrokePen, fillRect, (float)cornerRadius);
            else
                ctx.DrawRectangle(_dsp._dockIndicatorStrokePen, rect.Value, (float)cornerRadius);
        }
    }

    private class DummyTabItem(DockSpacePanel dsp) : TabItem 
    {
        public override void Render(DrawingContext ctx)
        {
            ctx.DrawRectangle(dsp._dockIndicatorStrokePen, Bounds.WithX(0).WithY(0), 4);
        }
    }
}
