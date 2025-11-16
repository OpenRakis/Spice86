using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace Avalonia.UpDock.Controls;

/// <summary>
/// A <see cref="DockPanel"/> in which the individual docked elements can be resized via split lines
/// </summary>
public class ResizeDockPanel : DockPanel
{
    private static readonly Cursor s_horizontalResizeCursor = new(StandardCursorType.SizeWestEast);
    private static readonly Cursor s_verticalResizeCursor = new(StandardCursorType.SizeNorthSouth);

    private (int index, Dock dock, Point lastPointerPosition)? _draggedSplitLine;

    private readonly List<Line> _splitLines = [];

    protected override void ChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        base.ChildrenChanged(sender, e);

        VisualChildren.RemoveAll(_splitLines);
        _splitLines.Clear();

        for (int i = 0; i < Children.Count - 1; i++)
        {
            var line = new Line()
            {
                Stroke = Brushes.Gray,
                StrokeThickness = 4
            };
            _splitLines.Add(line);
            VisualChildren.Add(line);
        }
    }

    protected override void ArrangeCore(Rect finalRect)
    {
        base.ArrangeCore(finalRect);

        for (var i = 0; i < Children.Count - 1; i++)
        {
            var child = Children[i];
            var bounds = child.Bounds;
            var splitLine = _splitLines[i];

            switch (GetDock(child))
            {
                case Dock.Left:
                    splitLine.StartPoint = bounds.TopRight;
                    splitLine.EndPoint = bounds.BottomRight;
                    splitLine.Cursor = s_horizontalResizeCursor;
                    break;
                case Dock.Right:
                    splitLine.StartPoint = bounds.TopLeft;
                    splitLine.EndPoint = bounds.BottomLeft;
                    splitLine.Cursor = s_horizontalResizeCursor;
                    break;
                case Dock.Top:
                    splitLine.StartPoint = bounds.BottomLeft;
                    splitLine.EndPoint = bounds.BottomRight;
                    splitLine.Cursor = s_verticalResizeCursor;
                    break;
                case Dock.Bottom:
                    splitLine.StartPoint = bounds.TopLeft;
                    splitLine.EndPoint = bounds.TopRight;
                    splitLine.Cursor = s_verticalResizeCursor;
                    break;
                default:
                    break;
            }

            splitLine.InvalidateVisual();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.Source is not Line line)
            return;

        int splitIndex = _splitLines.IndexOf(line);
        if (splitIndex == -1)
            return;

        _draggedSplitLine = (splitIndex, GetDock(Children[splitIndex]), e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        _draggedSplitLine = null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_draggedSplitLine == null)
            return;

        var pos = e.GetPosition(this);
        var (index, dock, lastPointerPosition) = _draggedSplitLine.Value;
        var delta = pos - lastPointerPosition;

        var child = Children[index];
        var visual = child.Bounds.Size;
        var previousSetSize = new Size(child.Width, child.Height);
            
        //kinda hacky but it seems to work
        child.Width = double.NaN;
        child.Height = double.NaN;
        child.Measure(visual);
        var desired = child.DesiredSize;

        double newWidth = previousSetSize.Width;
        double newHeight = previousSetSize.Height;

        double margin = 10;

        double maxSize = default;
        switch (dock)
        {
            case Dock.Left:
                maxSize = Bounds.Width - child.Bounds.Left - margin;
                newWidth = Math.Clamp(visual.Width + delta.X, desired.Width, maxSize);
                break;
            case Dock.Right:
                maxSize = child.Bounds.Right - margin;
                newWidth = Math.Clamp(visual.Width - delta.X, desired.Width, maxSize);
                break;
            case Dock.Top:
                maxSize = Bounds.Height - child.Bounds.Top - margin;
                newHeight = Math.Clamp(visual.Height + delta.Y, desired.Height, maxSize);
                break;
            case Dock.Bottom:
                maxSize = child.Bounds.Bottom - margin;
                newHeight = Math.Clamp(visual.Height - delta.Y, desired.Height, maxSize);
                break;
        }
        child.Width = newWidth;
        child.Height = newHeight;
        if (dock is Dock.Left or Dock.Right && (newWidth == desired.Width || newWidth == maxSize))
            return; //nothing changed visually
        if (dock is Dock.Top or Dock.Bottom && (newHeight == desired.Height || newHeight == maxSize))
            return; //nothing changed visually

        _draggedSplitLine = (index, dock, pos);
    }
}
