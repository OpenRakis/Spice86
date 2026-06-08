namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Statement;

/// <summary>The statement arm: an ordered sequence of statement items with no indentation baked in.</summary>
internal sealed record StatementsCode(IReadOnlyList<StatementItem> Items) : EmittedCode;
