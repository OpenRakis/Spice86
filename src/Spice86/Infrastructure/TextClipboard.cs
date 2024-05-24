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
