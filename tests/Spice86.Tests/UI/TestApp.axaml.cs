namespace Spice86.Tests.UI;

using Avalonia;
using Avalonia.Markup.Xaml;

public partial class TestApp : Application {
    public override void Initialize() {
        AvaloniaXamlLoader.Load(this);
    }
}