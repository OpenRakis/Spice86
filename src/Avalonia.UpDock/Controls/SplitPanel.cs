using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Avalonia.UpDock.Controls;

public class SplitFractions(params int[] fractions)
{
    public static SplitFractions Default => new(1);
    public int Count => fractions.Length;

    public (int offset, int size)[] CalcFractionLayoutInfos(int totalSize)
    {
        int denominator = fractions.Sum();
        var layoutInfos = new (int offset, int size)[fractions.Length];
        int offset = 0;
        for (int i = 0; i < fractions.Length; i++)
        {
            int size = (int)Math.Round(fractions[i] * totalSize / (double)denominator);
            layoutInfos[i] = (offset, size);
            offset += size;
        }

        layoutInfos[^1].size += totalSize - offset;

        return layoutInfos;
    }

    public int[] CalcFractionSizes(int totalSize) =>
        CalcFractionLayoutInfos(totalSize)
        .Select(x => x.size)
        .ToArray();

    public Rect[] CalcFractionRects(Size totalSize, Orientation orientation)
    {
        if (orientation == Orientation.Horizontal)
            return CalcFractionLayoutInfos((int)totalSize.Width)
                .Select(x => new Rect(x.offset, 0, x.size, totalSize.Height))
                .ToArray();
        else
            return CalcFractionLayoutInfos((int)totalSize.Height)
                .Select(x => new Rect(0, x.offset, totalSize.Width, x.size))
                .ToArray();
    }

    public int this[int index] => fractions[index];

    public static SplitFractions Parse(string s)
    {
        var tokenizer = new StringTokenizer(s);

        List<int> fractions = [];
        while(tokenizer.TryReadString(out var fractionStr))
        {
            fractions.Add(int.Parse(fractionStr));
        }

        return new SplitFractions([.. fractions]);
    }
}

public class SplitPanel : Panel
{
    private (int index, Point lastPointerPosition)? _draggedSplitLine = null;

    private static Cursor s_horizontalResizeCursor = new Cursor(StandardCursorType.SizeWestEast);
    private static Cursor s_verticalResizeCursor = new Cursor(StandardCursorType.SizeNorthSouth);

    private List<Line> _splitLines = [];
    private SplitFractions _fractions = SplitFractions.Default;
    private Orientation _orientation = Orientation.Horizontal;

    public int SlotCount => Fractions.Count;

    public SplitFractions Fractions
    {
        get => _fractions;
        set
        {
            var oldCount = _fractions.Count;
            if (value == null || value.Count == 0)
                _fractions = SplitFractions.Default;
            else
                _fractions = value;

            if (oldCount != _fractions.Count)
            {
                VisualChildren.RemoveAll(_splitLines);

                _splitLines.Clear();
                for (int i = 0; i < _fractions.Count - 1; i++)
                {
                    var line = new Line()
                    {
                        Stroke = Brushes.Gray,
                        StrokeThickness = 4
                    };

                    if (Orientation == Orientation.Horizontal)
                        line.Cursor = s_horizontalResizeCursor;
                    else
                        line.Cursor = s_verticalResizeCursor;

                    VisualChildren.Add(line);
                    _splitLines.Add(line);
                }
            }

            InvalidateMeasure();
        }
    }

    public Orientation Orientation
    {
        get => _orientation; 
        set
        {
            if (_orientation == value) return;
            _orientation = value;

            foreach (var line in _splitLines)
                line.Cursor = value == Orientation.Horizontal ? s_horizontalResizeCursor : s_verticalResizeCursor;

            InvalidateArrange();
        }
    }

    public Control? GetControlAtSlot(Index slot)
    {
        int idx = slot.GetOffset(Fractions.Count);

        if (idx < 0 || idx >= Fractions.Count)
            throw new ArgumentOutOfRangeException(nameof(slot), idx, null);

        return idx < Children.Count ? Children[idx] : null;
    }

    public void GetSlotSize(int slot, out int size, out Size size2D)
    {
        var sizes = Fractions.CalcFractionSizes(
            (int)ExtractForOrientation(Bounds.Size));

        size = sizes[slot];

        if (Orientation == Orientation.Horizontal)
        {
            size2D = new Size(size, Bounds.Height);
        }
        else
        {
            size2D = new Size(Bounds.Width, size);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var rects = _fractions.CalcFractionRects(availableSize, Orientation);
        var slotCount = Fractions.Count;

        double desiredWidth = 0;
        double desiredHeight = 0;
        for (int i = 0; i < Math.Min(Children.Count, slotCount); i++)
        {
            Children[i].Measure(rects[i].Size);
            if (Orientation == Orientation.Horizontal)
            {
                desiredWidth += Children[i].DesiredSize.Width;
                desiredHeight = Math.Max(desiredHeight, Children[i].DesiredSize.Height);
            }
            else
            {
                desiredWidth = Math.Max(desiredWidth, Children[i].DesiredSize.Width);
                desiredHeight += Children[i].DesiredSize.Height;
            }
        }

        return new Size(desiredWidth, desiredHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var rects = _fractions.CalcFractionRects(finalSize, Orientation);
        var slotCount = Fractions.Count;

        for (int i = 0; i < slotCount - 1; i++)
        {
            if (Orientation == Orientation.Horizontal)
            {
                
                _splitLines[i].StartPoint = rects[i].TopRight;
                _splitLines[i].EndPoint = rects[i].BottomRight;
            }
            else
            {
                _splitLines[i].StartPoint = rects[i].BottomLeft;
                _splitLines[i].EndPoint = rects[i].BottomRight;
            }

            _splitLines[i].InvalidateVisual();
        }

        for (int i = 0; i < Math.Min(Children.Count, slotCount); i++)
            Children[i].Arrange(rects[i]);

        return finalSize;
    }

    public bool TrySplitSlot(int slot, (Dock dock, int fraction, Control item) insert, int remainingSlotFraction)
    {
        if (insert.dock is Dock.Left or Dock.Right && Orientation is Orientation.Vertical)
            return false;
        if (insert.dock is Dock.Top or Dock.Bottom && Orientation is Orientation.Horizontal)
            return false;

        List<int> slotSizes =
        [
            .. _fractions.CalcFractionSizes(
                        (int)ExtractForOrientation(Bounds.Size)),
        ];

        int total = insert.fraction + remainingSlotFraction;

        int insertSlotSize = slotSizes[slot] * insert.fraction / total;
        slotSizes[slot] = slotSizes[slot] - insertSlotSize;

        if (insert.dock is Dock.Right or Dock.Bottom)
            slot++;

        slotSizes.Insert(slot, insertSlotSize);
        Children.Insert(slot, insert.item);
        Fractions = new SplitFractions([.. slotSizes]);
        return true;
    }

    public void RemoveSlot(int slot)
    {
        List<int> fractions = Enumerable.Range(0, Fractions.Count)
            .Select(i => Fractions[i])
            .ToList();

        fractions.RemoveAt(slot);

        Fractions = new SplitFractions([.. fractions]);

        if (Children.Count > slot)
            Children.RemoveAt(slot);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_draggedSplitLine == null)
            return;

        bool isHorizontal = Orientation == Orientation.Horizontal;

        var pointerPos = e.GetPosition(this);
        var (splitIndex, lastPointerPosition) = _draggedSplitLine.Value;
        var pointerDelta = new Point(
            Math.Round(pointerPos.X - lastPointerPosition.X),
            Math.Round(pointerPos.Y - lastPointerPosition.Y)
            );

        int[] fractionSizes = _fractions.CalcFractionSizes(
            (int)ExtractForOrientation(Bounds.Size));

        int delta = (int)(isHorizontal ? pointerDelta.X : pointerDelta.Y);

        int minFractionSizeSlotBefore = 20;
        int minFractionSizeSlotAfter = 20;

        if (splitIndex < Children.Count)
        {
            minFractionSizeSlotBefore = Math.Max(
                minFractionSizeSlotBefore,
                (int)ExtractForOrientation(Children[splitIndex].DesiredSize)
            );
        }
        if (splitIndex + 1 < Children.Count)
        {
            minFractionSizeSlotAfter = Math.Max(
                minFractionSizeSlotAfter,
                (int)ExtractForOrientation(Children[splitIndex + 1].DesiredSize)
            );
        }

        if (fractionSizes[splitIndex] + delta < minFractionSizeSlotBefore)
            delta = -(fractionSizes[splitIndex] - minFractionSizeSlotBefore);
        if (fractionSizes[splitIndex + 1] - delta < minFractionSizeSlotAfter)
            delta = (fractionSizes[splitIndex + 1] - minFractionSizeSlotAfter);

        if (fractionSizes[splitIndex] + delta < minFractionSizeSlotBefore) //we are trapped, abort
            return;

        fractionSizes[splitIndex] += delta;
        fractionSizes[splitIndex + 1] -= delta;

        if (isHorizontal)
            pointerDelta = pointerDelta.WithX(delta);
        else
            pointerDelta = pointerDelta.WithY(delta);

        _draggedSplitLine = (splitIndex, lastPointerPosition + pointerDelta);

        Fractions = new SplitFractions(fractionSizes);
    }

    private double ExtractForOrientation(Size size)
    {
        return Orientation == Orientation.Horizontal ? size.Width : size.Height;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.Source is not Line line)
            return;

        int splitIndex = _splitLines.IndexOf(line);
        if (splitIndex == -1)
            return;

        Debug.WriteLine($"Clicked on seperator between slot {splitIndex} and {splitIndex + 1}");

        _draggedSplitLine = (splitIndex, e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _draggedSplitLine = null;
    }
}
