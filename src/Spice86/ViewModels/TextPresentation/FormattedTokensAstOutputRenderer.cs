namespace Spice86.ViewModels.TextPresentation;

using Iced.Intel;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;

public class FormattedTextTokensAstOutputRenderer : IAstOutputRenderer<List<FormattedTextToken>> {
    private static List<FormattedTextToken> Token(string text, FormatterTextKind kind) {
        return [new() { Text = text, Kind = kind }];
    }

    public List<FormattedTextToken> Empty() {
        return [];
    }

    public bool IsEmpty(List<FormattedTextToken> output) {
        return output.Count == 0;
    }

    public List<FormattedTextToken> Text(string text) {
        return Token(text, FormatterTextKind.Text);
    }

    public List<FormattedTextToken> Mnemonic(string text) {
        return Token(text, FormatterTextKind.Mnemonic);
    }

    public List<FormattedTextToken> Number(string text) {
        return Token(text, FormatterTextKind.Number);
    }

    public List<FormattedTextToken> Register(string text) {
        return Token(text, FormatterTextKind.Register);
    }

    public List<FormattedTextToken> Keyword(string text) {
        return Token(text, FormatterTextKind.Keyword);
    }

    public List<FormattedTextToken> Operator(string text) {
        return Token(text, FormatterTextKind.Operator);
    }

    public List<FormattedTextToken> Punctuation(string text) {
        return Token(text, FormatterTextKind.Punctuation);
    }

    public List<FormattedTextToken> Prefix(string text) {
        return Token(text, FormatterTextKind.Prefix);
    }

    public List<FormattedTextToken> FunctionAddress(string text) {
        return Token(text, FormatterTextKind.FunctionAddress);
    }

    public List<FormattedTextToken> Concat(params List<FormattedTextToken>[] values) {
        int totalCount = 0;
        foreach (List<FormattedTextToken> value in values) {
            totalCount += value.Count;
        }

        List<FormattedTextToken> result = new(totalCount);
        foreach (List<FormattedTextToken> value in values) {
            result.AddRange(value);
        }

        return result;
    }

    public string ToPlainText(List<FormattedTextToken> output) {
        if (output.Count == 0) {
            return string.Empty;
        }

        string[] texts = new string[output.Count];
        for (int i = 0; i < output.Count; i++) {
            texts[i] = output[i].Text;
        }
        return string.Concat(texts);
    }
}
