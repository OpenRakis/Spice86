using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Spice86.UserControls;

using Avalonia.Collections;
using Avalonia.Controls.Shapes;

using Spice86.Shared;
using Spice86.Shared.Emulator.Video;

public partial class PaletteUserControl : UserControl {
    public PaletteUserControl() {
        InitializeComponent();
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }
    public static readonly StyledProperty<AvaloniaList<Rgb>> PaletteProperty =
        AvaloniaProperty.Register<PaletteUserControl, AvaloniaList<Rgb>>(nameof(Palette));

    public AvaloniaList<Rgb> Palette
    {
        get { return GetValue(PaletteProperty); }
        set { SetValue(PaletteProperty, value); }
    }
}