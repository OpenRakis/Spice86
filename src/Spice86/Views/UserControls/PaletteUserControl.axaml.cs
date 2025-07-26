namespace Spice86.Views.UserControls;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;

using Spice86.Shared.Emulator.Video;

/// <summary>
/// Code-behind file for the display of RGB colors.
/// </summary>
public partial class PaletteUserControl : UserControl {
    /// <summary>
    /// Initializes a new instance of the <see cref="PaletteUserControl"/> class.
    /// </summary>
    public PaletteUserControl() => InitializeComponent();

    /// <summary>
    /// Defines a <see cref="StyledProperty{TValue}"/> for the <see cref="Palette"/> property.
    /// </summary>
    public static readonly StyledProperty<AvaloniaList<Rgb>> PaletteProperty =
        AvaloniaProperty.Register<PaletteUserControl, AvaloniaList<Rgb>>(nameof(Palette));

    /// <summary>
    /// Gets or sets the palette of RGB colors.
    /// </summary>
    public AvaloniaList<Rgb> Palette {
        get { return GetValue(PaletteProperty); }
        set { SetValue(PaletteProperty, value); }
    }
}