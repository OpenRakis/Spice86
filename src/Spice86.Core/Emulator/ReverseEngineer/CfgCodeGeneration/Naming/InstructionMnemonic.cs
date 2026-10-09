namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Naming;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

/// <summary>
/// Derives the mnemonic of an instruction as a lower-cased identifier token, so generated symbols such as
/// signature fields read like the assembly they were decoded from.
/// </summary>
internal sealed class InstructionMnemonic(IAstVisitor<string> assemblyRenderer) {
    /// <summary>
    /// Returns the mnemonic of <paramref name="instruction"/> as an identifier token: the instruction is
    /// rendered without its <c>rep</c>/<c>repe</c>/<c>repne</c> prefix so a prefixed string instruction
    /// yields its own mnemonic instead of <c>rep</c>, the text is cut at the first space, lower-cased, and
    /// every character that is not an ASCII letter or digit is replaced by <c>_</c>.
    /// </summary>
    public string Of(CfgInstruction instruction) {
        string rendered = (instruction.DisplayAst with { RepPrefix = null }).Accept(assemblyRenderer);
        int firstSpace = rendered.IndexOf(' ');
        string mnemonic = firstSpace < 0 ? rendered : rendered[..firstSpace];
        return Sanitize(mnemonic.ToLowerInvariant());
    }

    private static string Sanitize(string mnemonic) {
        char[] chars = mnemonic.ToCharArray();
        for (int i = 0; i < chars.Length; i++) {
            if (!char.IsAsciiLetterOrDigit(chars[i])) {
                chars[i] = '_';
            }
        }
        return new string(chars);
    }
}
