namespace Spice86.Infrastructure;

using Avalonia.Input.Platform;

/// <inheritdoc cref="ITextClipboard" />
public class TextClipboard : ITextClipboard {
    private readonly IClipboard? _clipboard;

    public TextClipboard(IClipboard? clipboard) => _clipboard = clipboard;

    /// <inheritdoc />
    public async Task SetTextAsync(string? text) {
        if (_clipboard is null) {
            return;
        }
        await _clipboard.SetTextAsync(text);
    }
}

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
