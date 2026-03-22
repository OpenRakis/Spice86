namespace Spice86.ViewModels.TextPresentation;

using Iced.Intel;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;

/// <summary>
/// <see cref="IRenderer"/> implementation that accumulates output as a list of
/// <see cref="FormattedToken"/> objects, each carrying a <see cref="FormatterTextKind"/> annotation
/// for theme-aware syntax highlighting.
/// </summary>
public class TokensRenderer : IRenderer {
    private readonly List<FormattedToken> _tokens = [];

    /// <inheritdoc/>
    public void WriteText(string text) => Add(text, FormatterTextKind.Text);

    /// <inheritdoc/>
    public void WriteMnemonic(string mnemonic) => Add(mnemonic, FormatterTextKind.Mnemonic);

    /// <inheritdoc/>
    public void WriteNumber(string number) => Add(number, FormatterTextKind.Number);

    /// <inheritdoc/>
    public void WriteRegister(string register) => Add(register, FormatterTextKind.Register);

    /// <inheritdoc/>
    public void WriteKeyword(string keyword) => Add(keyword, FormatterTextKind.Keyword);

    /// <inheritdoc/>
    public void WriteOperator(string op) => Add(op, FormatterTextKind.Operator);

    /// <inheritdoc/>
    public void WritePunctuation(string text) => Add(text, FormatterTextKind.Punctuation);

    /// <inheritdoc/>
    public void WriteFunctionAddress(string address) => Add(address, FormatterTextKind.FunctionAddress);

    /// <inheritdoc/>
    public void WritePrefix(string prefix) => Add(prefix, FormatterTextKind.Prefix);

    private void Add(string text, FormatterTextKind kind) =>
        _tokens.Add(new FormattedToken { Text = text, Kind = kind });

    /// <summary>Clears the accumulated tokens so the instance can be reused.</summary>
    public void Reset() => _tokens.Clear();

    /// <summary>Returns the accumulated list of tokens.</summary>
    public List<FormattedToken> GetTokens() => _tokens;
}
