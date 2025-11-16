using System;
using Avalonia.Controls;

namespace Avalonia.UpDock;

/// <summary>
/// Makes a hooked up window follow another window's movement
/// </summary>
internal class ChildWindowMoveHandler
{
    private (PixelPoint topLeft, Size size) _lastParentWindowBounds;
    private readonly Window _child;
    private readonly Window _parent;
    
    /// <summary>
    /// Makes <paramref name="child"/> follow the movement of <paramref name="parent"/> indefinitly
    /// </summary>
    /// <param name="parent">The window to follow</param>
    /// <param name="child">The window to hook up</param>
    public static void Hookup(Window parent, Window child)
    {
        var handler = new ChildWindowMoveHandler(parent, child);
        parent.PositionChanged += handler.Parent_PositionChanged;
        child.Closed += handler.Child_Closed;
    }

    private void Child_Closed(object? sender, EventArgs e)
    {
        _parent.PositionChanged -= Parent_PositionChanged;
        _child.Closed -= Child_Closed;
    }

    private void Parent_PositionChanged(object? sender, PixelPointEventArgs e)
    {
        var position = _parent.Position;
        var size = _parent.FrameSize.GetValueOrDefault();
        if (_lastParentWindowBounds.size != size)
        {
            _lastParentWindowBounds = (position, size);
            return;
        }

        var delta = position - _lastParentWindowBounds.topLeft;

        _child.Position += delta;
        _lastParentWindowBounds = (position, size);
    }

    private ChildWindowMoveHandler(Window parent, Window child)
    {
        _child = child;
        _parent = parent;
        _lastParentWindowBounds = (parent.Position, parent.FrameSize.GetValueOrDefault());
    }
}