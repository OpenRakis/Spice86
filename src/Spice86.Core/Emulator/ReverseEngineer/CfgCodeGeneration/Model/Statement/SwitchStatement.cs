namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Statement;

using System.Linq;

/// <summary>
/// A C# <c>switch</c> over a value, used for runtime near jump/call dispatch over observed targets. Each
/// case body is followed by an implicit <c>break;</c>; the default body is rendered as-is (it always throws,
/// so no trailing break is emitted).
/// </summary>
internal sealed record SwitchStatement(string Header, IReadOnlyList<SwitchCase> Cases, IReadOnlyList<StatementItem> Default) : StatementItem {
    // The renderer emits `break;` only after a case body that completes normally (a diverging body already
    // transfers control, so a trailing break would be unreachable). Control therefore reaches the statement
    // following the switch exactly when some case body completes normally (and breaks) or the default body
    // does. When every case and the default diverge, the switch end point is unreachable, so it does not
    // complete normally.
    public override bool CompletesNormally =>
        Cases.Any(switchCase => EmittedCode.SequenceCompletesNormally(switchCase.Body))
        || EmittedCode.SequenceCompletesNormally(Default);
}
