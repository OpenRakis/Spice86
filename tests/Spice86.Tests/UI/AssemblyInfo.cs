using Avalonia.Headless;
using Avalonia.Headless.XUnit;

using Spice86.Tests.UI;

using Xunit;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]
[assembly: CollectionBehavior(DisableTestParallelization = true)]
