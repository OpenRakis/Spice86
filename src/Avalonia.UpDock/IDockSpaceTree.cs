using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.UpDock.Controls;

namespace Avalonia.UpDock;

public interface IDraggedOutTabHolder
{
    Size TabContentSize { get; }
    Size TabItemSize { get; }
    Size TabControlSize { get; }
    TabItem RetrieveTabItem();
}

public interface IDockSpaceTree<TNode>
    where TNode : struct, IEquatable<TNode>
{
    bool CanFill(TNode node);
    bool CanSplit(TNode node);
    
    /// <summary>
    /// Gives information about where new nodes can be docked and what nodes they need to be docked in
    /// to fulfill the <see cref="IDockSpaceTree{TNode}"/>s constraints
    /// </summary>
    DockingLocations<TNode> GetDockingLocations(TNode node);

    void VisitDockingTreeNodes(Action<TNode> visitor);
    bool HitTestTabItem(TNode node, Point point, out int index, out Rect tabItemRect);
    Rect GetDockAreaBounds(TNode node);
    
    TNode GetRootNode();
    
    internal static Rect CalculateDockRect(Size sourceSize, Rect targetBounds, Dock dock, bool isSplit)
    {
        double clampedWidth = Math.Min(sourceSize.Width, isSplit ? targetBounds.Width / 2 : targetBounds.Width);
        double clampedHeight = Math.Min(sourceSize.Height, isSplit ? targetBounds.Height / 2 : targetBounds.Height);

        return dock switch
        {
            Dock.Left => targetBounds.WithWidth(clampedWidth),
            Dock.Top => targetBounds.WithHeight(clampedHeight),
            Dock.Right => new Rect(
                targetBounds.TopLeft.WithX(targetBounds.Right - clampedWidth),
                targetBounds.BottomRight),
            Dock.Bottom => new Rect(
                targetBounds.TopLeft.WithY(targetBounds.Bottom - clampedHeight),
                targetBounds.BottomRight),
            _ => throw new InvalidEnumArgumentException(nameof(dock), (int)dock, typeof(Dock)),
        };
    }
}