namespace Spice86;

using Avalonia;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;

/// <summary>
/// The main entry point for the Spice86 UI.
/// </summary>
internal partial class App : Application {

    /// <summary>
    /// Initializes the Spice86 UI.
    /// </summary>
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    
    public override void OnFrameworkInitializationCompleted() {
        
        base.OnFrameworkInitializationCompleted();
    }
}