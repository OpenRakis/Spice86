namespace Spice86.Views.Controls;

using Avalonia;
using Avalonia.Controls;

using Spice86.ViewModels.DataModels;

/// <summary>A hierarchical grid control for displaying <see cref="StructureMemberNode" /> trees.</summary>
public partial class StructureHierarchyGrid : UserControl {
    /// <summary>Defines the <see cref="Items" /> styled property.</summary>
    public static readonly StyledProperty<IEnumerable<StructureMemberNode>?> ItemsProperty =
        AvaloniaProperty.Register<StructureHierarchyGrid, IEnumerable<StructureMemberNode>?>(nameof(Items));

    /// <summary>Gets or sets the root items to display.</summary>
    public IEnumerable<StructureMemberNode>? Items {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    /// <summary>Initializes a new instance of <see cref="StructureHierarchyGrid" />.</summary>
    public StructureHierarchyGrid() {
        InitializeComponent();
    }
}
