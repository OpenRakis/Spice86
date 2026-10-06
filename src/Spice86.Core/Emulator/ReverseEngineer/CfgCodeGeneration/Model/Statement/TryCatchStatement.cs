namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Statement;

/// <summary>
/// A <c>try</c> block followed by one <c>catch</c> block whose full header (e.g. <c>catch (CpuException cpuException)</c>) is <paramref name="CatchHeader"/>.
/// </summary>
internal sealed record TryCatchStatement(IReadOnlyList<StatementItem> TryBody, string CatchHeader, IReadOnlyList<StatementItem> CatchBody) : StatementItem {
    public override bool CompletesNormally =>
        EmittedCode.SequenceCompletesNormally(TryBody) || EmittedCode.SequenceCompletesNormally(CatchBody);

    public override IEnumerable<IReadOnlyList<StatementItem>> NestedBodies => [TryBody, CatchBody];
}