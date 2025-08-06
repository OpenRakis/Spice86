namespace Spice86.Core.CLI;

public enum HeadlessType {
    /// <summary>
    ///     Use the minimal headless mode, which doesn't render any UI elements
    /// </summary>
    Minimal,

    /// <summary>
    ///     Use Avalonia headless mode, which uses the full UI and consumes a bit more memory
    /// </summary>
    Avalonia
}