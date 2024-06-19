namespace Spice86;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using Microsoft.Extensions.DependencyInjection;

using Spice86.Core.CLI;
using Spice86.DependencyInjection;
using Spice86.Infrastructure;
using Spice86.Shared.Interfaces;
using Spice86.ViewModels;
using Spice86.Views;

/// <summary>
/// The main entry point for the Spice86 UI.
/// </summary>
internal partial class App : Application {

    /// <summary>
    /// Initializes the Spice86 UI.
    /// </summary>
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted() {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) {
            throw new PlatformNotSupportedException("Spice86 requires a desktop application lifetime.");
        }
        base.OnFrameworkInitializationCompleted();
    }
}