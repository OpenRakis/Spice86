namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Statement;

/// <summary>One case of a <see cref="SwitchStatement"/>: a <c>case &lt;label&gt;:</c> guard and its body.</summary>
internal sealed record SwitchCase(string Label, IReadOnlyList<StatementItem> Body);
