using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Spice86.UserControls;

using Avalonia.Collections;
using Avalonia.Controls.Shapes;

using Spice86.Shared;
using Spice86.Shared.Emulator.Video;

/// <summary>
/// Code-behind file for the display of RGB colors.
/// </summary>
public partial class PaletteUserControl : UserControl {
    
    /// <summary>
    /// Initializes a new instance of the <see cref="PaletteUserControl"/> class.
    /// </summary>
    public PaletteUserControl() {
        InitializeComponent();
    }

    /// <summary>
    /// Loads the XAML content of the control.
    /// </summary>
    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }
    
    /// <summary>
    /// Defines a <see cref="StyledProperty{TValue}"/> for the <see cref="Palette"/> property.
    /// </summary>
    public static readonly StyledProperty<AvaloniaList<Rgb>> PaletteProperty =
        AvaloniaProperty.Register<PaletteUserControl, AvaloniaList<Rgb>>(nameof(Palette));

    /// <summary>
    /// Gets or sets the palette of RGB colors.
    /// </summary>
    public AvaloniaList<Rgb> Palette
    {
        get { return GetValue(PaletteProperty); }
        set { SetValue(PaletteProperty, value); }
    }
}
