namespace Spice86.Tests.UI;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;

using System;

public class TestApp : Application {
    private const string Spice86ControlThemesSource = "avares://Spice86/Views/Assets/ControlThemes.axaml";
    private const string Spice86StylesSource = "avares://Spice86/Views/Styles/Spice86.axaml";
    
    public override void Initialize() {
        AvaloniaXamlLoader.Load(this);
    }
    
    public override void OnFrameworkInitializationCompleted() {
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

public static class TestAppBuilder {
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
