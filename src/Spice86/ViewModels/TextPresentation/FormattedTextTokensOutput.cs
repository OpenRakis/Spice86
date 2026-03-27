namespace Spice86.ViewModels.TextPresentation;

using Iced.Intel;

/// <summary>
/// Thread-safe formatter output that doesn't create UI elements.
/// </summary>
public class FormattedTextTokensOutput : FormatterOutput {
    /// <summary>
    /// Gets the list of formatted text offsets.
    /// </summary>
    public List<FormattedTextToken> TextOffsets { get; } = [];

    /// <summary>
    /// Writes a text offset with the specified kind.
    /// </summary>
    /// <param name="text">The text to write.</param>
    /// <param name="kind">The kind of text.</param>
    public override void Write(string text, FormatterTextKind kind) {
        TextOffsets.Add(new FormattedTextToken {
            Text = text,
            Kind = kind
        });
    }
}