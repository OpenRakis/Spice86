using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Spice86.UserControls;

using Avalonia.Controls.Shapes;

public partial class PaletteUserControl : UserControl {
    public PaletteUserControl() {
        InitializeComponent();
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }
    public static readonly StyledProperty<IEnumerable<Rectangle>> PaletteProperty =
        AvaloniaProperty.Register<PaletteUserControl, IEnumerable<Rectangle>>(nameof(Palette));

    public IEnumerable<Rectangle> Palette
    {
        get { return GetValue(PaletteProperty); }
        set { SetValue(PaletteProperty, value); }
    }
}