using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;

namespace Avalonia.UpDock;

/// <summary>
/// see <see cref="IDockSpaceTree{TNode}.GetDockingLocations"/>
/// </summary>
/// <typeparam name="TNode"></typeparam>
public record struct DockingLocations<TNode>
    where TNode : struct
{
    public bool TryGetDockingLocation(Dock dock, [NotNullWhen(true)] out TNode? dockingLocation)
    {
        dockingLocation = dock switch
        {
            Dock.Left => Left,
            Dock.Bottom => Bottom,
            Dock.Right => Right,
            Dock.Top => Top,
            _ => throw new InvalidEnumArgumentException(nameof(dock), (int)dock, typeof(Dock))
        };

        return dockingLocation != null;
    }
    
    public TNode? Top;
    public TNode? Left;
    public TNode? Bottom;
    public TNode? Right;
    public static readonly DockingLocations<TNode> Empty = new();
    public static DockingLocations<TNode> All(TNode node) => new()
    {
        Top = node, Left = node, Bottom = node, Right = node
    };
}