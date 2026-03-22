namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;

using System.Text;

/// <summary>
/// <see cref="IRenderer"/> implementation that accumulates all written fragments into a plain string.
/// </summary>
public class StringRenderer : IRenderer {
    private readonly StringBuilder _sb = new();

    /// <inheritdoc/>
    public void WriteText(string text) => _sb.Append(text);

    /// <inheritdoc/>
    public void WriteMnemonic(string mnemonic) => _sb.Append(mnemonic);

    /// <inheritdoc/>
    public void WriteNumber(string number) => _sb.Append(number);

    /// <inheritdoc/>
    public void WriteRegister(string register) => _sb.Append(register);

    /// <inheritdoc/>
    public void WriteKeyword(string keyword) => _sb.Append(keyword);

    /// <inheritdoc/>
    public void WriteOperator(string op) => _sb.Append(op);

    /// <inheritdoc/>
    public void WritePunctuation(string text) => _sb.Append(text);

    /// <inheritdoc/>
    public void WriteFunctionAddress(string address) => _sb.Append(address);

    /// <inheritdoc/>
    public void WritePrefix(string prefix) => _sb.Append(prefix);

    /// <summary>Clears the accumulated output so the instance can be reused.</summary>
    public void Reset() => _sb.Clear();

    /// <summary>Returns the accumulated output as a string.</summary>
    public string GetResult() => _sb.ToString();
}
