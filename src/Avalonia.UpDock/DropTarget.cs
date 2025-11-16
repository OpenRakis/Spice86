using Avalonia.Controls;
using System;

namespace Avalonia.UpDock;

public struct DropTarget : IEquatable<DropTarget>
{
    private enum Kind
    {
        None,
        Fill,
        SplitDock,
        NeighborDock,
        TabBar
    }

    private Kind _kind;

    private Dock _dock;
    private int _tabIndex;

    public static DropTarget None => new();

    public static DropTarget SplitDock(Dock dock)
        => new() { _kind = Kind.SplitDock, _dock = dock };
    public static DropTarget NeighborDock(Dock dock)
        => new() { _kind = Kind.NeighborDock, _dock = dock };
    public static DropTarget Fill
        => new() { _kind = Kind.Fill };
    public static DropTarget TabBar(int tabIndex)
        => new() { _kind = Kind.TabBar, _tabIndex = tabIndex };

    public readonly bool IsNone() => _kind == Kind.None;

    public readonly bool IsSplitDock(Dock dock) => IsSplitDock(out var value) && value == dock;
    public readonly bool IsSplitDock(out Dock dock)
    {
        dock = _dock;
        return _kind == Kind.SplitDock;
    }

    public readonly bool IsNeighborDock(Dock dock) => IsNeighborDock(out var value) && value == dock;
    public readonly bool IsNeighborDock(out Dock dock)
    {
        dock = _dock;
        return _kind == Kind.NeighborDock;
    }

    public readonly bool IsFill() => _kind == Kind.Fill;

    public readonly bool IsTabBar(int tabIndex) => IsTabBar(out var value) && value == tabIndex;
    public readonly bool IsTabBar(out int tabIndex)
    {
        tabIndex = _tabIndex;
        return _kind == Kind.TabBar;
    }

    public readonly bool Equals(DropTarget other)
    {
        return
            _dock == other._dock &&
            _kind == other._kind &&
            _tabIndex == other._tabIndex;
    }

    public static bool operator ==(DropTarget left, DropTarget right) => left.Equals(right);
    public static bool operator !=(DropTarget left, DropTarget right) => !left.Equals(right);

    public readonly override bool Equals(object? obj) => obj is DropTarget target && Equals(target);
    public readonly override int GetHashCode() => 0;
}
