namespace Spice86.Infrastructure;

/// <summary>
/// Provides access to the host clipboard
/// </summary>
public interface ITextClipboard {
    /// <summary>
    /// Sets the clipboard's text.
    /// </summary>
    /// <param name="text">The text to be copied to the clipboard.</param>
    /// <returns>A <c>Task</c> representing the async operation.</returns>
    Task SetTextAsync(string? text);
}
