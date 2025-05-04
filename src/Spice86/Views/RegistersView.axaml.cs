namespace Spice86.Views;

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

/// <summary>
/// View for displaying CPU registers.
/// </summary>
public partial class RegistersView : UserControl {
    /// <summary>
    /// Initializes a new instance of the <see cref="RegistersView"/> class.
    /// </summary>
    public RegistersView() {
        InitializeComponent();
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }
}