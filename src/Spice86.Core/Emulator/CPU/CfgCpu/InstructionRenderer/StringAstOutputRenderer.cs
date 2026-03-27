namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;

public class StringAstOutputRenderer : IAstOutputRenderer<string> {
    public string Empty() {
        return string.Empty;
    }

    public bool IsEmpty(string output) {
        return output.Length == 0;
    }

    public string Text(string text) {
        return text;
    }

    public string Mnemonic(string text) {
        return text;
    }

    public string Number(string text) {
        return text;
    }

    public string Register(string text) {
        return text;
    }

    public string Keyword(string text) {
        return text;
    }

    public string Operator(string text) {
        return text;
    }

    public string Punctuation(string text) {
        return text;
    }

    public string Prefix(string text) {
        return text;
    }

    public string FunctionAddress(string text) {
        return text;
    }

    public string Concat(params string[] values) {
        return string.Concat(values);
    }

    public string ToPlainText(string output) {
        return output;
    }
}
