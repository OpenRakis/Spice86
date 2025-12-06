namespace Spice86.Tests.UI;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;

using System;

/// <summary>
/// Test application for headless UI testing.
/// </summary>
public class TestApp : Application {
    private const string Spice86ControlThemesSource = "avares://Spice86/Views/Assets/ControlThemes.axaml";
    private const string Spice86StylesSource = "avares://Spice86/Views/Styles/Spice86.axaml";

    /// <inheritdoc/>
    public override void Initialize() {
        // Load SemiAvalonia theme and styles
        AvaloniaXamlLoader.Load(this);
    }

    /// <inheritdoc/>
    public override void OnFrameworkInitializationCompleted() {
        // Load app resources that are normally loaded by the real App
        LoadAppResources();
        base.OnFrameworkInitializationCompleted();
    }

    private static void LoadAppResources() {
        IResourceDictionary appResources = Application.Current!.Resources;

        ResourceInclude controlThemes = new(new Uri(Spice86ControlThemesSource)) {
            Source = new Uri(Spice86ControlThemesSource)
        };
        appResources.MergedDictionaries.Add(controlThemes);

        Avalonia.Styling.Styles appStyles = Application.Current!.Styles;
        appStyles.Add(new StyleInclude(new Uri(Spice86StylesSource)) {
            Source = new Uri(Spice86StylesSource)
        });
    }
}

/// <summary>
/// Configures the Avalonia application for headless UI testing.
/// </summary>
public static class TestAppBuilder {
    /// <summary>
    /// Builds the Avalonia application configured for headless testing.
    /// </summary>
    /// <returns>An <see cref="AppBuilder"/> configured for headless operation.</returns>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}