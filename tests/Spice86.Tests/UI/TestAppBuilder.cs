namespace Spice86.Tests.UI;

using Avalonia;
using Avalonia.Headless;

public static class TestAppBuilder {
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
