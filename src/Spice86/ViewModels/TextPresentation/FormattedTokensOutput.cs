namespace Spice86.ViewModels.TextPresentation;

using Iced.Intel;

/// <summary>
/// Thread-safe <see cref="FormatterOutput"/> that collects Iced disassembler output
/// as a list of <see cref="FormattedToken"/> objects without creating any UI elements.
/// </summary>
public class FormattedTokensOutput : FormatterOutput {
    /// <summary>
    /// Gets the list of formatted tokens.
    /// </summary>
    public List<FormattedToken> Tokens { get; } = [];

    /// <summary>
    /// Writes a token with the specified kind.
    /// </summary>
    /// <param name="text">The text to write.</param>
    /// <param name="kind">The kind of text.</param>
    public override void Write(string text, FormatterTextKind kind) {
        Tokens.Add(new FormattedToken {
            Text = text,
            Kind = kind
        });
    }
}
