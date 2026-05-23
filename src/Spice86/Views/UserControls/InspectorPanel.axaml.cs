namespace Spice86.Views.UserControls;

using Avalonia;
using Avalonia.Controls;

/// <summary>
/// Reusable host for a single <see cref="Avalonia.PropertyGrid.Controls.PropertyGrid"/> bound to an Info object.
/// </summary>
public partial class InspectorPanel : UserControl {
    /// <summary>
    /// The Info object rendered by the inner property grid.
    /// </summary>
    public static readonly StyledProperty<object?> DataProperty =
        AvaloniaProperty.Register<InspectorPanel, object?>(nameof(Data));

    /// <summary>
    /// The header displayed above the property grid.
    /// </summary>
    public static readonly StyledProperty<string> HeaderProperty =
        AvaloniaProperty.Register<InspectorPanel, string>(nameof(Header), string.Empty);

    /// <inheritdoc cref="DataProperty"/>
    public object? Data {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <inheritdoc cref="HeaderProperty"/>
    public string Header {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>Initializes a new <see cref="InspectorPanel"/>.</summary>
    public InspectorPanel() {
        InitializeComponent();
    }
}
