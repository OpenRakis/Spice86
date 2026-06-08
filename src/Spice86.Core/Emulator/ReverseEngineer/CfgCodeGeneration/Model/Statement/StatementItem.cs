namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Statement;

/// <summary>
/// A single item of generated C# in statement position. The renderer walks these mechanically; none of them
/// carry indentation, which the <see cref="CSharpSourceWriter"/> manages.
/// </summary>
internal abstract record StatementItem {
    /// <summary>
    /// Whether control can fall through past this item to the next statement. Drives method-body completion
    /// analysis (see <see cref="EmittedCode.CompletesNormally"/>) so the trailing untested-failure throw is
    /// emitted only for a body that can actually fall off its end.
    /// </summary>
    public abstract bool CompletesNormally { get; }
}
