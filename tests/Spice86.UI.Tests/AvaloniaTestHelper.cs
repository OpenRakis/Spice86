using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Threading;
using System;
using System.Threading.Tasks;

namespace Spice86.UI.Tests;

/// <summary>
/// Helper class for Avalonia UI testing
/// </summary>
public static class AvaloniaTestHelper
{
    private static bool _isInitialized;

    /// <summary>
    /// Initializes the Avalonia framework for testing
    /// </summary>
    public static void Initialize()
    {
        if (_isInitialized)
        {
            return;
        }

        AppBuilder.Configure<Application>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .SetupWithoutStarting();

        _isInitialized = true;
    }

    /// <summary>
    /// Runs a UI test with a window
    /// </summary>
    /// <param name="testAction">The test action to run</param>
    public static async Task RunTestWithWindow(Func<Window, Task> testAction)
    {
        Initialize();

        var window = new Window();
        window.Show();

        try
        {
            await testAction(window);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => window.Close());
        }
    }
}
