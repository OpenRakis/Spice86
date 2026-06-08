namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;

/// <summary>The expression arm: a single C# expression carrying type and precedence (for cast/paren elision).</summary>
internal sealed record ExpressionCode(CSharpFragment Fragment) : EmittedCode;
