namespace Spice86.Views;

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

/// <summary>
/// View for the modern disassembly interface.
/// </summary>
public partial class ModernDisassemblyView : UserControl {
    /// <summary>
    /// Initializes a new instance of the <see cref="ModernDisassemblyView"/> class.
    /// </summary>
    public ModernDisassemblyView() {
        InitializeComponent();
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }
}