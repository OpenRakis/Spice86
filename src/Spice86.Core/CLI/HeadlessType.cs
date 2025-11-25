namespace Spice86.Core.CLI;

/// <summary>
/// Specifies the type of headless mode for running the emulator without a graphical user interface.
/// </summary>
/// <remarks>
/// Headless mode is useful for automated testing, continuous integration, or when running on servers without display capabilities.
/// The <see cref="Minimal"/> mode has the smallest memory footprint, while <see cref="Avalonia"/> provides more features at the cost of additional memory usage.
/// </remarks>
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