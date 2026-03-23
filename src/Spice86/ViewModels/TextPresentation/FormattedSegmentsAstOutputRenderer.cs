namespace Spice86.ViewModels.TextPresentation;

using Iced.Intel;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;

public class FormattedSegmentsAstOutputRenderer : IAstOutputRenderer<List<FormattedTextSegment>> {
    private static List<FormattedTextSegment> Segment(string text, FormatterTextKind kind) {
        return [new() { Text = text, Kind = kind }];
    }

    public List<FormattedTextSegment> Empty() {
        return [];
    }

    public bool IsEmpty(List<FormattedTextSegment> output) {
        return output.Count == 0;
    }

    public List<FormattedTextSegment> Text(string text) {
        return Segment(text, FormatterTextKind.Text);
    }

    public List<FormattedTextSegment> Mnemonic(string text) {
        return Segment(text, FormatterTextKind.Mnemonic);
    }

    public List<FormattedTextSegment> Number(string text) {
        return Segment(text, FormatterTextKind.Number);
    }

    public List<FormattedTextSegment> Register(string text) {
        return Segment(text, FormatterTextKind.Register);
    }

    public List<FormattedTextSegment> Keyword(string text) {
        return Segment(text, FormatterTextKind.Keyword);
    }

    public List<FormattedTextSegment> Operator(string text) {
        return Segment(text, FormatterTextKind.Operator);
    }

    public List<FormattedTextSegment> Punctuation(string text) {
        return Segment(text, FormatterTextKind.Punctuation);
    }

    public List<FormattedTextSegment> Prefix(string text) {
        return Segment(text, FormatterTextKind.Prefix);
    }

    public List<FormattedTextSegment> FunctionAddress(string text) {
        return Segment(text, FormatterTextKind.FunctionAddress);
    }

    public List<FormattedTextSegment> Concat(params List<FormattedTextSegment>[] values) {
        int totalCount = 0;
        foreach (List<FormattedTextSegment> value in values) {
            totalCount += value.Count;
        }

        List<FormattedTextSegment> result = new(totalCount);
        foreach (List<FormattedTextSegment> value in values) {
            result.AddRange(value);
        }

        return result;
    }

    public string ToPlainText(List<FormattedTextSegment> output) {
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
