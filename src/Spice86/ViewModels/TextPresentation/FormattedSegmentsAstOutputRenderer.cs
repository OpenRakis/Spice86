namespace Spice86.ViewModels.TextPresentation;

using Iced.Intel;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;

public class FormattedTextOffsetsAstOutputRenderer : IAstOutputRenderer<List<FormattedTextOffset>> {
    private static List<FormattedTextOffset> Offset(string text, FormatterTextKind kind) {
        return [new() { Text = text, Kind = kind }];
    }

    public List<FormattedTextOffset> Empty() {
        return [];
    }

    public bool IsEmpty(List<FormattedTextOffset> output) {
        return output.Count == 0;
    }

    public List<FormattedTextOffset> Text(string text) {
        return Offset(text, FormatterTextKind.Text);
    }

    public List<FormattedTextOffset> Mnemonic(string text) {
        return Offset(text, FormatterTextKind.Mnemonic);
    }

    public List<FormattedTextOffset> Number(string text) {
        return Offset(text, FormatterTextKind.Number);
    }

    public List<FormattedTextOffset> Register(string text) {
        return Offset(text, FormatterTextKind.Register);
    }

    public List<FormattedTextOffset> Keyword(string text) {
        return Offset(text, FormatterTextKind.Keyword);
    }

    public List<FormattedTextOffset> Operator(string text) {
        return Offset(text, FormatterTextKind.Operator);
    }

    public List<FormattedTextOffset> Punctuation(string text) {
        return Offset(text, FormatterTextKind.Punctuation);
    }

    public List<FormattedTextOffset> Prefix(string text) {
        return Offset(text, FormatterTextKind.Prefix);
    }

    public List<FormattedTextOffset> FunctionAddress(string text) {
        return Offset(text, FormatterTextKind.FunctionAddress);
    }

    public List<FormattedTextOffset> Concat(params List<FormattedTextOffset>[] values) {
        int totalCount = 0;
        foreach (List<FormattedTextOffset> value in values) {
            totalCount += value.Count;
        }

        List<FormattedTextOffset> result = new(totalCount);
        foreach (List<FormattedTextOffset> value in values) {
            result.AddRange(value);
        }

        return result;
    }

    public string ToPlainText(List<FormattedTextOffset> output) {
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
