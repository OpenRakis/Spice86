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

    public override void RegisterServices() {
        
        base.RegisterServices();
        
    }

    public override void OnFrameworkInitializationCompleted() {
        // If you use CommunityToolkit, line below is needed to remove Avalonia data validation.
        // Without this line you will get duplicate validations from both Avalonia and CT
        BindingPlugins.DataValidators.RemoveAt(0);
        
        base.OnFrameworkInitializationCompleted();
    }
}