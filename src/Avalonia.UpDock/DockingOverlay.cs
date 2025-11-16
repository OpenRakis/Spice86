using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.UpDock.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Avalonia.UpDock;

public class DockingOverlay<TDockNode>
    where TDockNode : struct, IEquatable<TDockNode>
{
    public readonly struct StyleParameters
    {
        public double FieldSize { get; init; }
        public double FieldSpacing { get; init; }
    }
    
    public enum DockUIElement
    {
        None,
        LeftSplitRect,
        RightSplitRect,
        TopSplitRect,
        BottomSplitRect,
        CenterRect,

        LeftNeighborRect,
        RightNeighborRect,
        TopNeighborRect,
        BottomNeighborRect,
        
        TabItemRect
    }

    public struct DockUIInfo
    {
        public Rect AreaBounds;
        public DockUIElement HoveredElement;
        public double CornerRadiusScaling;
        public Rect? LeftSplitRect;
        public Rect? RightSplitRect;
        public Rect? TopSplitRect;
        public Rect? BottomSplitRect;
        public Rect? CenterRect;

        public Rect? LeftNeighborRect;
        public Rect? RightNeighborRect;
        public Rect? TopNeighborRect;
        public Rect? BottomNeighborRect;
        
        public Rect? TabItemRect;
    }

    public readonly struct TabDropInfo(TDockNode? node, DropTarget dropTarget)
    {
        public bool IsSplitControl([NotNullWhen(true)] out TDockNode? target, out Dock dock)
        {
            target = node;
            bool isCorrectTarget = dropTarget.IsSplitDock(out dock);
            return target != null && isCorrectTarget;
        }
        public bool IsFillControl([NotNullWhen(true)] out TDockNode? target)
        {
            target = node;
            return target != null && dropTarget.IsFill();
        }
        public bool IsInsertNextTo([NotNullWhen(true)] out TDockNode? target, out Dock dock)
        {
            target = node;
            bool isCorrectTarget = dropTarget.IsNeighborDock(out dock);
            return target != null && isCorrectTarget;
        }

        public bool IsInsertOuter(out Dock dock)
        {
            bool isCorrectTarget = dropTarget.IsNeighborDock(out dock);
            return node == null && isCorrectTarget;
        }

        public bool IsInsertTab([NotNullWhen(true)] out TDockNode? target, out int index)
        {
            target = node;
            bool isCorrectTarget = dropTarget.IsTabBar(out index);
            return target != null && isCorrectTarget;
        }
    }

    public record TabDroppedEventArgs(TabDropInfo DropInfo, IDraggedOutTabHolder TabHolder);
    public record AreaEnteredEventArgs(TDockNode Node);
    public record AreaExitedEventArgs(TDockNode Node);

    public event EventHandler<AreaEnteredEventArgs>? AreaEntered;
    public event EventHandler<AreaExitedEventArgs>? AreaExited;
    public event EventHandler<TabDroppedEventArgs>? TabDropped;
    public event EventHandler<EventArgs>? UIChanged;

    public DockingOverlay(IDockSpaceTree<TDockNode> dockSpaceTree, in StyleParameters styleParameters)
    {
        _styleParameters = styleParameters;
        _dockSpaceTree = dockSpaceTree;
    }

    private readonly IDockSpaceTree<TDockNode> _dockSpaceTree;
    private readonly StyleParameters _styleParameters;
    private readonly List<DockAreaInfo> _areas = [];
    private DockAreaUIInfo? _hoveredDockArea;
    private DropTarget _hoveredDropTarget;
    private DockAreaUIInfo? _rootDockArea;
    private (DockUIInfo? outerEdges, DockUIInfo? hoveredArea, Rect? dockedElementBounds) _uiState = (null, null, null);

    public (DockUIInfo? outerEdges, DockUIInfo? hoveredArea, Rect? dockedElementBounds) GetUI()
    {
        return _uiState;
    }

    public void UpdateAreas()
    {
        DockAreaInfo GetAreaInfo(TDockNode node)
        {
            return new DockAreaInfo(node)
            {
                Bounds = _dockSpaceTree.GetDockAreaBounds(node),
                DockingLocations = _dockSpaceTree.GetDockingLocations(node),
                CanSplit = _dockSpaceTree.CanSplit(node),
                CanFill = _dockSpaceTree.CanFill(node)
            };
        }
        
        _areas.Clear();
        _dockSpaceTree.VisitDockingTreeNodes(node => _areas.Add(GetAreaInfo(node)));

        var root = _dockSpaceTree.GetRootNode();
        var rootDockAreaInfo = GetAreaInfo(root);
        _rootDockArea = new DockAreaUIInfo
        { 
            BaseInfo = rootDockAreaInfo, 
            DockUI = CalcDockUILayout(rootDockAreaInfo, isForOuterEdges: true)
        };
        
        UIChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DropTab(Point position, IDraggedOutTabHolder draggedOutTabHolder)
    {
        var hoveredAreaBefore = _hoveredDockArea;
        DragOver(position, draggedOutTabHolder, suppressUIChangeEvent: true);
        
        if (!_hoveredDropTarget.IsNone())
        {
            TabDropped?.Invoke(this, new TabDroppedEventArgs(
                new TabDropInfo(_hoveredDockArea?.BaseInfo.DockNode, _hoveredDropTarget), 
                draggedOutTabHolder
            ));
        }
        
        if (hoveredAreaBefore != null)
            AreaExited?.Invoke(this, new AreaExitedEventArgs(hoveredAreaBefore.Value.BaseInfo.DockNode));
        
        _hoveredDockArea = null;
        _hoveredDropTarget = DropTarget.None;
        
        UIChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DragOver(Point position, IDraggedOutTabHolder draggedOutTabHolder)
        => DragOver(position, draggedOutTabHolder, suppressUIChangeEvent: false);
    
    private void DragOver(Point position, IDraggedOutTabHolder draggedOutTabHolder, bool suppressUIChangeEvent)
    {
        int newHoveredAreaIdx = _areas.FindLastIndex(x => x.Bounds.Contains(position));
        DockAreaUIInfo? newHoveredDockArea = null;
        var newHoveredDropTarget = DropTarget.None;
        Rect? dockedElementBoundsIndicator = null;
        
        if (newHoveredAreaIdx != -1)
        {
            var areaInfo = _areas[newHoveredAreaIdx];
            var dockUI = CalcDockUILayout(areaInfo);

            if (_dockSpaceTree.HitTestTabItem(areaInfo.DockNode, position, out int tabIndex, out var tabItemRect))
            {
                dockUI.TabItemRect = new Rect(tabItemRect.TopLeft, draggedOutTabHolder.TabItemSize);
                dockUI.HoveredElement = DockUIElement.TabItemRect;
                newHoveredDropTarget = DropTarget.TabBar(tabIndex);
            }
            
            var hoveredElement = EvaluateHoveredElement(position, dockUI);
            if (hoveredElement != DockUIElement.None)
            {
                dockUI.HoveredElement = hoveredElement;
                newHoveredDropTarget = AsDropTarget(hoveredElement);
                dockedElementBoundsIndicator = GetDockedElementBounds(areaInfo, newHoveredDropTarget, draggedOutTabHolder);
            }
            
            newHoveredDockArea = new DockAreaUIInfo { BaseInfo = areaInfo, DockUI = dockUI };
        }
        else
            newHoveredDropTarget = DropTarget.None;

        var outerEdgesDropTarget = DropTarget.None;
        if (_rootDockArea != null)
        {
            var areaInfo = _rootDockArea.Value.BaseInfo;
            var dockUI = _rootDockArea.Value.DockUI;
            
            var hoveredElement = EvaluateHoveredElement(position, dockUI);
            dockUI.HoveredElement = hoveredElement;
            _rootDockArea = new DockAreaUIInfo { BaseInfo = areaInfo, DockUI = dockUI };
            
            outerEdgesDropTarget = AsDropTarget(hoveredElement);
        }
        
        if (!outerEdgesDropTarget.IsNone() && _rootDockArea != null)
        {
            newHoveredDockArea = null;
            newHoveredDropTarget = outerEdgesDropTarget;
            dockedElementBoundsIndicator = GetDockedElementBounds(_rootDockArea.Value.BaseInfo, 
                newHoveredDropTarget, draggedOutTabHolder);
        }

        bool hoveredDropTargetChanged = 
            !Nullable.Equals(newHoveredDockArea?.BaseInfo.DockNode, _hoveredDockArea?.BaseInfo.DockNode) ||
            newHoveredDropTarget != _hoveredDropTarget;
        
        if (!hoveredDropTargetChanged)
            return;

        _uiState = (_rootDockArea?.DockUI, newHoveredDockArea?.DockUI, dockedElementBoundsIndicator);

        if (_hoveredDockArea != null)
            AreaExited?.Invoke(this, new AreaExitedEventArgs(_hoveredDockArea.Value.BaseInfo.DockNode));

        if (newHoveredDockArea != null)
            AreaEntered?.Invoke(this, new AreaEnteredEventArgs(newHoveredDockArea.Value.BaseInfo.DockNode));

        _hoveredDockArea = newHoveredDockArea;
        _hoveredDropTarget = newHoveredDropTarget;

        if (!suppressUIChangeEvent)
            UIChanged?.Invoke(this, EventArgs.Empty);
    }

    private DockUIInfo CalcDockUILayout(DockAreaInfo areaInfo,
        bool isForOuterEdges = false)
    {
        static Rect AlignLeft(Rect rect, Rect bounds, bool onlyClamp = false) =>
            !onlyClamp || rect.Left < bounds.Left ?
                rect.Translate(Vector.UnitX * (bounds.Left - rect.Left)) : rect;

        static Rect AlignRight(Rect rect, Rect bounds, bool onlyClamp = false) =>
            !onlyClamp || rect.Right > bounds.Right ?
                rect.Translate(Vector.UnitX * (bounds.Right - rect.Right)) : rect;

        static Rect AlignTop(Rect rect, Rect bounds, bool onlyClamp = false) =>
            !onlyClamp || rect.Top < bounds.Top ?
                rect.Translate(Vector.UnitY * (bounds.Top - rect.Top)) : rect;

        static Rect AlignBottom(Rect rect, Rect bounds, bool onlyClamp = false) =>
            !onlyClamp || rect.Bottom > bounds.Bottom ?
                rect.Translate(Vector.UnitY * (bounds.Bottom - rect.Bottom)) : rect;
        
        static Rect DockUIRect(ReadOnlySpan<Rect> rects, int x, int y)
        {
            int col = Math.Clamp(x, -1, 1) + 1;
            int row = Math.Clamp(y, -1, 1) + 1;

            var rect = rects[col + row * 3];

            var offset = rect.Center - rects[4].Center;
            //handle x and y values outside the rects
            rect = rect.Translate(-offset);
            return rect.Translate(new Vector(offset.X * Math.Abs(x), offset.Y * Math.Abs(y)));
        }

        Span<Rect> rects = stackalloc Rect[9];
        var dockingLocations = areaInfo.DockingLocations;
        Calculate3X3DockUIRectsWithMargin(areaInfo.Bounds, rects, out double cornerRadiusScaling);

        var uiInfo = new DockUIInfo
        {
            CornerRadiusScaling = cornerRadiusScaling,
            AreaBounds = areaInfo.Bounds
        };

        if (isForOuterEdges)
        {
            uiInfo.LeftNeighborRect = AlignLeft(DockUIRect(rects, -2, 0), areaInfo.Bounds);
            uiInfo.RightNeighborRect = AlignRight(DockUIRect(rects, 2, 0), areaInfo.Bounds);
            uiInfo.TopNeighborRect = AlignTop(DockUIRect(rects, 0, -2), areaInfo.Bounds);
            uiInfo.BottomNeighborRect = AlignBottom(DockUIRect(rects, 0, 2), areaInfo.Bounds);

            return uiInfo;
        }

        if (areaInfo.CanSplit)
        {
            uiInfo.LeftSplitRect = DockUIRect(rects, -1, 0);
            uiInfo.RightSplitRect = DockUIRect(rects, 1, 0);
            uiInfo.TopSplitRect = DockUIRect(rects, 0, -1);
            uiInfo.BottomSplitRect = DockUIRect(rects, 0, 1);
        }

        if (areaInfo.CanFill)
            uiInfo.CenterRect = DockUIRect(rects, 0, 0);

        if (dockingLocations.TryGetDockingLocation(Dock.Left, out _))
            uiInfo.LeftNeighborRect = AlignLeft(DockUIRect(rects, -2, 0), areaInfo.Bounds, onlyClamp: true);

        if (dockingLocations.TryGetDockingLocation(Dock.Right, out _))
            uiInfo.RightNeighborRect = AlignRight(DockUIRect(rects, 2, 0), areaInfo.Bounds, onlyClamp: true);

        if (dockingLocations.TryGetDockingLocation(Dock.Top, out _))
            uiInfo.TopNeighborRect = AlignTop(DockUIRect(rects, 0, -2), areaInfo.Bounds, onlyClamp: true);

        if (dockingLocations.TryGetDockingLocation(Dock.Bottom, out _))
            uiInfo.BottomNeighborRect = AlignBottom(DockUIRect(rects, 0, 2), areaInfo.Bounds, onlyClamp: true);

        return uiInfo;
    }

    private static DockUIElement EvaluateHoveredElement(Point pos, in DockUIInfo uiInfo)
    {
        var uiElement = DockUIElement.None;
        if (uiInfo.CenterRect?.Contains(pos) == true)
            uiElement = DockUIElement.CenterRect;
        
        if (uiInfo.LeftSplitRect?.Contains(pos) == true)
            uiElement =  DockUIElement.LeftSplitRect;
        if (uiInfo.RightSplitRect?.Contains(pos) == true)
            uiElement =  DockUIElement.RightSplitRect;
        if (uiInfo.TopSplitRect?.Contains(pos) == true)
            uiElement =  DockUIElement.TopSplitRect;
        if (uiInfo.BottomSplitRect?.Contains(pos) == true)
            uiElement =  DockUIElement.BottomSplitRect;
        
        if (uiInfo.LeftNeighborRect?.Contains(pos) == true)
            uiElement =  DockUIElement.LeftNeighborRect;
        if (uiInfo.RightNeighborRect?.Contains(pos) == true)
            uiElement =  DockUIElement.RightNeighborRect;
        if (uiInfo.TopNeighborRect?.Contains(pos) == true)
            uiElement =  DockUIElement.TopNeighborRect;
        if (uiInfo.BottomNeighborRect?.Contains(pos) == true)
            uiElement =  DockUIElement.BottomNeighborRect;
        
        return uiElement;
    }

    private static DropTarget AsDropTarget(DockUIElement uiElement) =>
        uiElement switch
        {
            DockUIElement.CenterRect => DropTarget.Fill,
            
            DockUIElement.LeftSplitRect => DropTarget.SplitDock(Dock.Left),
            DockUIElement.RightSplitRect => DropTarget.SplitDock(Dock.Right),
            DockUIElement.TopSplitRect => DropTarget.SplitDock(Dock.Top),
            DockUIElement.BottomSplitRect => DropTarget.SplitDock(Dock.Bottom),
            
            DockUIElement.LeftNeighborRect => DropTarget.NeighborDock(Dock.Left),
            DockUIElement.RightNeighborRect => DropTarget.NeighborDock(Dock.Right),
            DockUIElement.TopNeighborRect => DropTarget.NeighborDock(Dock.Top),
            DockUIElement.BottomNeighborRect => DropTarget.NeighborDock(Dock.Bottom),
            _ => DropTarget.None
        };
    
    private Rect? GetDockedElementBounds(DockAreaInfo areaInfo, DropTarget dropTarget, 
        IDraggedOutTabHolder draggedTabInfo)
    {
        if (dropTarget.IsNone())
            return null;
        if (dropTarget.IsFill())
            return areaInfo.Bounds;
        if (dropTarget.IsSplitDock(out var dock))
            return IDockSpaceTree<TDockNode>.CalculateDockRect(draggedTabInfo.TabContentSize, areaInfo.Bounds, dock, true);
        if (dropTarget.IsNeighborDock(out dock))
        {
            var areaBounds = areaInfo.Bounds;
            if (areaInfo.DockingLocations.TryGetDockingLocation(dock, out var dockingLocation))
                areaBounds = _dockSpaceTree.GetDockAreaBounds(dockingLocation.Value);
            
            return IDockSpaceTree<TDockNode>.CalculateDockRect(draggedTabInfo.TabContentSize, areaBounds, dock, false);
        }
        
        Debug.Fail("Bad drop target");
        return null;
    }

    private void Calculate3X3DockUIRectsWithMargin(Rect bounds, Span<Rect> rects,
        out double cornerScaling)
    {
        double totalSize = 
            _styleParameters.FieldSize * 5 + 
            _styleParameters.FieldSpacing * 2;

        double scaling = Math.Min(bounds.Width, bounds.Height) / totalSize;
        scaling = Math.Min(scaling, 1);

        var fieldSizeScaled = new Size(_styleParameters.FieldSize, _styleParameters.FieldSize) * scaling;
        double spacingScaled = _styleParameters.FieldSpacing * scaling;

        double distance = fieldSizeScaled.Width + spacingScaled;

        for (var x = 0; x < 3; x++)
        {
            for (var y = 0; y < 3; y++)
            {
                rects[x + y * 3] = bounds.CenterRect(new Rect(fieldSizeScaled))
                    .Translate(new Vector(distance * (x - 1), distance * (y - 1)));
            }
        }

        cornerScaling = scaling;
    }
    
    private struct DockAreaUIInfo
    {
        public DockAreaInfo BaseInfo;
        public DockUIInfo DockUI;
    }
    private struct DockAreaInfo
    {
        public readonly TDockNode DockNode;
        public Rect Bounds;
        public DockingLocations<TDockNode> DockingLocations;
        public bool CanSplit;
        public bool CanFill;
        
        

        public DockAreaInfo(TDockNode dockNode)
        {
            DockNode = dockNode;
        }
    }
}
