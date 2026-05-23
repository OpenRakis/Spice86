namespace Spice86.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

using Spice86.ViewModels;
using Spice86.ViewModels.DataModels;
using Spice86.Views.UserControls;

using System.Collections.ObjectModel;
using System.Collections.Specialized;

/// <summary>Code-behind for the DOS MCBs view. Renders the conventional memory layout bar from the VM's blocks.</summary>
public partial class DosMcbsView : EmulatorObjectUserControl {
    private const string FreeBrushKey = "SystemControlBackgroundBaseLowBrush";
    private const string UsedBrushKey = "SystemAccentColorBrush";
    private const string LastEdgeBrushKey = "SystemControlHighlightAltAccentBrush";
    private const string SeparatorBrushKey = "SystemControlForegroundBaseLowBrush";

    private DosMcbsViewModel? _attachedVm;

    /// <summary>Initializes a new <see cref="DosMcbsView"/>.</summary>
    public DosMcbsView() {
        InitializeComponent();
        ActualThemeVariantChanged += OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, System.EventArgs e) {
        if (_attachedVm is not null) {
            RebuildBar(_attachedVm.Blocks);
        }
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e) {
        base.OnDataContextChanged(e);
        if (_attachedVm is not null) {
            _attachedVm.Blocks.CollectionChanged -= OnBlocksChanged;
        }
        _attachedVm = DataContext as DosMcbsViewModel;
        if (_attachedVm is not null) {
            if (IsViewModelEffectivelyVisible) {
                _attachedVm.Blocks.CollectionChanged += OnBlocksChanged;
            }
            RebuildBar(_attachedVm.Blocks);
        } else {
            BarHost.Children.Clear();
            BarHost.ColumnDefinitions.Clear();
        }
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
        base.OnAttachedToVisualTree(e);
        if (_attachedVm is not null) {
            _attachedVm.Blocks.CollectionChanged -= OnBlocksChanged;
            _attachedVm.Blocks.CollectionChanged += OnBlocksChanged;
            RebuildBar(_attachedVm.Blocks);
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
        if (_attachedVm is not null) {
            _attachedVm.Blocks.CollectionChanged -= OnBlocksChanged;
        }
        base.OnDetachedFromVisualTree(e);
    }

    private void OnBlocksChanged(object? sender, NotifyCollectionChangedEventArgs e) {
        if (_attachedVm is not null) {
            RebuildBar(_attachedVm.Blocks);
        }
    }

    private IBrush? FindBrush(string key) {
        if (this.TryFindResource(key, ActualThemeVariant, out object? resource)
            && resource is IBrush brush) {
            return brush;
        }
        return null;
    }

    private void RebuildBar(ObservableCollection<DosMcbBarItem> blocks) {
        BarHost.Children.Clear();
        BarHost.ColumnDefinitions.Clear();
        if (blocks.Count == 0) {
            return;
        }
        long total = 0;
        foreach (DosMcbBarItem block in blocks) {
            total += block.SizeBytes;
        }
        if (total <= 0) {
            return;
        }
        IBrush? freeBrush = FindBrush(FreeBrushKey);
        IBrush? usedBrush = FindBrush(UsedBrushKey);
        IBrush? lastEdgeBrush = FindBrush(LastEdgeBrushKey);
        IBrush? separatorBrush = FindBrush(SeparatorBrushKey);
        int column = 0;
        foreach (DosMcbBarItem block in blocks) {
            double weight;
            if (block.SizeBytes <= 0) {
                weight = 1;
            } else {
                weight = block.SizeBytes;
            }
            IBrush? backgroundBrush;
            if (block.IsFree) {
                backgroundBrush = freeBrush;
            } else {
                backgroundBrush = usedBrush;
            }
            IBrush? borderBrush;
            Thickness borderThickness;
            if (block.IsLast) {
                borderBrush = lastEdgeBrush;
                borderThickness = new Thickness(0, 0, 3, 0);
            } else {
                borderBrush = separatorBrush;
                borderThickness = new Thickness(0, 0, 1, 0);
            }
            BarHost.ColumnDefinitions.Add(new ColumnDefinition(weight, GridUnitType.Star));
            Border cell = new() {
                Background = backgroundBrush,
                BorderBrush = borderBrush,
                BorderThickness = borderThickness
            };
            ToolTip.SetTip(cell, block.Tooltip);
            Grid.SetColumn(cell, column);
            BarHost.Children.Add(cell);
            column++;
        }
    }
}
