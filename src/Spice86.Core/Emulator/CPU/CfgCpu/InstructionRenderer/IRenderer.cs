namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;

/// <summary>
/// Abstraction for writing rendered AST output. Implementations decide how to accumulate each piece
/// (e.g. plain string concatenation or annotated tokens for syntax highlighting).
/// </summary>
public interface IRenderer {
    /// <summary>Writes a plain text fragment (spaces, newlines, etc.).</summary>
    void WriteText(string text);

    /// <summary>Writes an instruction mnemonic.</summary>
    void WriteMnemonic(string mnemonic);

    /// <summary>Writes a numeric constant.</summary>
    void WriteNumber(string number);

    /// <summary>Writes a register name.</summary>
    void WriteRegister(string register);

    /// <summary>Writes a keyword (e.g. type cast or pointer size prefix).</summary>
    void WriteKeyword(string keyword);

    /// <summary>Writes an operator symbol.</summary>
    void WriteOperator(string op);

    /// <summary>Writes a punctuation symbol (brackets, colon, comma, etc.).</summary>
    void WritePunctuation(string text);

    /// <summary>Writes a function/jump-target address.</summary>
    void WriteFunctionAddress(string address);

    /// <summary>Writes an instruction prefix (e.g. "rep", "repe").</summary>
    void WritePrefix(string prefix);
}
