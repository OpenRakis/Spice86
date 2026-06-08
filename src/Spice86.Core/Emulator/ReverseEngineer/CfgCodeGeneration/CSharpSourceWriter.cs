namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using System.Text;

internal sealed class CSharpSourceWriter {
    private readonly StringBuilder _builder = new();
    private int _indent;

    public void Line(string text = "") {
        if (text.Length > 0) {
            _builder.Append(' ', _indent * 4);
            _builder.Append(text);
        }
        _builder.AppendLine();
    }

    public void OpenBlock(string header) {
        Line(header + " {");
        _indent++;
    }

    /// <summary>Increases the current indentation by one level without emitting braces. Used to nest
    /// <c>switch</c> case bodies one level under their <c>case</c> label.</summary>
    public void Indent() => _indent++;

    /// <summary>Decreases the current indentation by one level. Pairs with <see cref="Indent"/>.</summary>
    public void Dedent() => _indent--;

    public void CloseBlock(string suffix = "") {
        _indent--;
        Line("}" + suffix);
    }

    public void Label(string label) {
        _builder.Append(' ', Math.Max(0, (_indent - 1) * 4));
        _builder.Append(label);
        _builder.AppendLine(":");
    }

    public override string ToString() => _builder.ToString();
}