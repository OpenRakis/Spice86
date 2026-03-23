namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;

public interface IAstOutputRenderer<TOutput> {
    public TOutput Empty();
    public bool IsEmpty(TOutput output);
    public TOutput Text(string text);
    public TOutput Mnemonic(string text);
    public TOutput Number(string text);
    public TOutput Register(string text);
    public TOutput Keyword(string text);
    public TOutput Operator(string text);
    public TOutput Punctuation(string text);
    public TOutput Prefix(string text);
    public TOutput FunctionAddress(string text);
    public TOutput Concat(params TOutput[] values);
    public string ToPlainText(TOutput output);
}
