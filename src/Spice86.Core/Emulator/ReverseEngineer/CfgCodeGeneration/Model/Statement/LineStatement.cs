namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Statement;

/// <summary>
/// A single source line (e.g. <c>CX = (ushort)0x0000;</c> or <c>goto label_X;</c>). <paramref name="Diverges"/>
/// marks lines that transfer control away and never fall through (<c>return</c>/<c>goto</c>/<c>throw</c>).
/// </summary>
internal sealed record LineStatement(string Text, bool Diverges = false) : StatementItem {
    public override bool CompletesNormally => !Diverges;
}
